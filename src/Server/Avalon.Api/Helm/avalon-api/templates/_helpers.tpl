{{/*
Expand the name of the chart.
*/}}
{{- define "avalon-api.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Create a default fully qualified app name.
We truncate at 63 chars because some Kubernetes name fields are limited to this (by the DNS naming spec).
If release name contains chart name it will be used as a full name.
*/}}
{{- define "avalon-api.fullname" -}}
{{- if .Values.fullnameOverride }}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- if contains $name .Release.Name }}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}
{{- end }}

{{/*
Create chart name and version as used by the chart label.
*/}}
{{- define "avalon-api.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Common labels
*/}}
{{- define "avalon-api.labels" -}}
helm.sh/chart: {{ include "avalon-api.chart" . }}
{{ include "avalon-api.selectorLabels" . }}
{{- if .Chart.AppVersion }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
{{- end }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{/*
Selector labels
*/}}
{{- define "avalon-api.selectorLabels" -}}
app.kubernetes.io/name: {{ include "avalon-api.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}

{{/*
Whether this is a routes release (#794, design D7.2): routes.enabled renders only the IngressRoute
and runs no API service, so it needs services: [] said outright. A release that leaves services out
runs all four services; turning routes on there by mistake would remove its pods.
*/}}
{{- define "avalon-api.routesMode" -}}
{{- if (.Values.routes | default dict).enabled }}
{{- if not (kindIs "slice" .Values.services) }}
{{- fail "routes.enabled renders only the IngressRoute and runs no API service, so give services: [] with it. Left out, services runs all four services, and routes.enabled would remove their pods." }}
{{- end }}
{{- if .Values.services }}
{{- fail (printf "routes.enabled renders only the IngressRoute and runs no API service, so services must be [] (it lists %s). A routes release reaches the services through routes.backends." (join ", " .Values.services)) }}
{{- end }}
{{- "true" }}
{{- end }}
{{- end }}

{{/*
The API services this release runs (#794, design D9.2), as a JSON list: the names services lists, in
lower case; every service of the route manifest (files/routes.json) when services is left out, which
renders no Application__Services, so the process runs them all as it did before the split; none in a
routes release. Refuses a services value that is not a list, an empty list outside a routes release,
a name the manifest does not know, and a name listed twice.
*/}}
{{- define "avalon-api.services" -}}
{{- $known := keys (.Files.Get "files/routes.json" | fromJson).services | sortAlpha }}
{{- if eq (include "avalon-api.routesMode" .) "true" }}
{{- list | toJson }}
{{- else if kindIs "invalid" .Values.services }}
{{- $known | toJson }}
{{- else }}
{{- if not (kindIs "slice" .Values.services) }}
{{- fail (printf "services is a list of the API services to run, any of %s." (join ", " $known)) }}
{{- end }}
{{- if not .Values.services }}
{{- fail (printf "services lists no service: name one or more of %s, or leave services out to run them all in one process." (join ", " $known)) }}
{{- end }}
{{- $listed := list }}
{{- range $name := .Values.services }}
{{- $service := $name | toString | trim | lower }}
{{- if not (has $service $known) }}
{{- fail (printf "services names %q, which is not an API service: the services are %s." (toString $name) (join ", " $known)) }}
{{- end }}
{{- if has $service $listed }}
{{- fail (printf "services lists %s twice." $service) }}
{{- end }}
{{- $listed = append $listed $service }}
{{- end }}
{{- $listed | toJson }}
{{- end }}
{{- end }}

{{/*
The parts of each world's databases the services in the list read (#794, design D9.2), as a JSON
list: worlds reads both, identity only the characters database, commerce and distribution none.
*/}}
{{- define "avalon-api.worldParts" -}}
{{- if has "worlds" . }}
{{- list "world" "characters" | toJson }}
{{- else if has "identity" . }}
{{- list "characters" | toJson }}
{{- else }}
{{- list | toJson }}
{{- end }}
{{- end }}

{{/*
Values refused in every mode: those that would put a provider credential through Helm, where only a
reference to a Secret is accepted; and the HS256 values #801 removed, since the API refuses HS256
access tokens whatever is configured and nothing reads them any more.
*/}}
{{- define "avalon-api.refuseSecretValues" -}}
{{- if hasKey (.Values.email | default dict) "resendApiKey" -}}
{{- fail "email.resendApiKey is forbidden; use email.existingSecret and email.resendApiKeyKey" -}}
{{- end -}}
{{- if hasKey (.Values.commerce | default dict) "apiKey" -}}{{- fail "commerce.apiKey is forbidden; use an existing Secret reference" -}}{{- end -}}
{{- if hasKey (.Values.commerce | default dict) "webhookSecret" -}}{{- fail "commerce.webhookSecret is forbidden; use an existing Secret reference" -}}{{- end -}}
{{- $auth := .Values.authentication | default dict -}}
{{- if hasKey $auth "legacyIssuerSigningKey" -}}
{{- fail "authentication.legacyIssuerSigningKey was removed (#801): access tokens are ES256 only and the API refuses HS256, so nothing reads the HS256 key any more. Remove the value, and remove the old key jwt-signing-key from the Secret (existingSecret): its value lives on as game-auth-host-key." -}}
{{- end -}}
{{- if $auth.issuerSigningKey -}}
{{- fail "authentication.issuerSigningKey, the HS256 key, was removed (#801): access tokens are ES256 only and the API refuses HS256. Remove it, and pass its value as gameAuth.hostKey, the game-auth host key." -}}
{{- end -}}
{{- end -}}

{{/*
The access tokens' keys (#801), called with (list <root> <identity>): key ids the API accepts (at
most 64 letters, digits, '.', '_' and '-'), each listed with a public key, never a private one
(validationKeys are plain values); identity's key id where identity runs, and at least one public
key where it does not, since such a process can check a token with nothing else.
*/}}
{{- define "avalon-api.validateSigning" -}}
{{- $root := index . 0 }}
{{- $identity := index . 1 }}
{{- $auth := $root.Values.authentication | default dict }}
{{- $validation := $auth.validationKeys | default dict }}
{{- if not (kindIs "map" $validation) }}
{{- fail "authentication.validationKeys maps each key id to the base64 of a public key (its SubjectPublicKeyInfo)." }}
{{- end }}
{{- range $kid, $key := $validation }}
{{- if not (regexMatch "^[A-Za-z0-9._-]{1,64}$" $kid) }}
{{- fail (printf "authentication.validationKeys.%s: a key id is at most 64 letters, digits, '.', '_' and '-'." $kid) }}
{{- end }}
{{- if contains "PRIVATE KEY" ($key | toString) }}
{{- fail (printf "authentication.validationKeys.%s holds a private key. List only the public key: these are plain values, and the private key stays in the Secret, for identity alone." $kid) }}
{{- end }}
{{- if not ($key | toString | trim) }}
{{- fail (printf "authentication.validationKeys.%s is empty: give the base64 of the public key (its SubjectPublicKeyInfo)." $kid) }}
{{- end }}
{{- end }}
{{- if $identity }}
{{- if not (regexMatch "^[A-Za-z0-9._-]{1,64}$" ($auth.signingKeyId | default "" | toString)) }}
{{- fail "authentication.signingKeyId is required where identity runs: the key id identity signs access tokens under (at most 64 letters, digits, '.', '_' and '-'), e.g. 2026-10." }}
{{- end }}
{{- else if not $validation }}
{{- fail "authentication.validationKeys is required where identity does not run: identity's public key, under its key id (authentication.signingKeyId), is all such a process checks access tokens with." }}
{{- end }}
{{- end }}

{{/*
The service that owns a path by the route manifest (design D2.3): the service of the longest rule
the path is or lies under, else the manifest's default. Called with (list <manifest> <path>), the
path in the manifest's normalised form.
*/}}
{{- define "avalon-api.ownerOf" -}}
{{- $manifest := index . 0 }}
{{- $path := index . 1 }}
{{- $owner := $manifest.default }}
{{- $longest := 0 }}
{{- range $service, $rules := $manifest.services }}
{{- range $rule := $rules }}
{{- if and (or (eq $path $rule) (hasPrefix (printf "%s/" $rule) $path)) (gt (len $rule) $longest) }}
{{- $owner = $service }}
{{- $longest = len $rule }}
{{- end }}
{{- end }}
{{- end }}
{{- $owner }}
{{- end }}

{{/*
Refuses a routes release that would route a request wrongly or nowhere (#794, design D7.2): no host,
a malformed prefix, Middleware or Service name, a backend missing for a service of the manifest or
named for none, and an override (design section 10) whose path is not written as the manifest
writes rules, is listed twice, is internal, belongs to another service by the manifest, or has
another service's rule under it, which the override would take, since it outranks every rule.
*/}}
{{- define "avalon-api.validateRoutes" -}}
{{- $manifest := .Files.Get "files/routes.json" | fromJson }}
{{- $known := keys $manifest.services | sortAlpha }}
{{- $routes := .Values.routes }}
{{- $name := "^[a-z0-9]([-a-z0-9]*[a-z0-9])?$" }}
{{- if or (not (kindIs "slice" $routes.hosts)) (not $routes.hosts) }}
{{- fail "routes.hosts lists no host: the IngressRoute answers only on the hosts it names, e.g. [avalon.example, admin.avalon.example]." }}
{{- end }}
{{- range $host := $routes.hosts }}
{{- if not (regexMatch "^[a-z0-9]([-a-z0-9]*[a-z0-9])?(\\.[a-z0-9]([-a-z0-9]*[a-z0-9])?)*$" (toString $host)) }}
{{- fail (printf "routes.hosts: %q is not a host name in lower case." (toString $host)) }}
{{- end }}
{{- end }}
{{- if not (regexMatch "^(/[-._~a-zA-Z0-9]+)*$" ($routes.pathPrefix | default "" | toString)) }}
{{- fail (printf "routes.pathPrefix is %q: a path such as /api, with no '/' at the end, or empty for the root." (toString $routes.pathPrefix)) }}
{{- end }}
{{- range $middleware := $routes.middlewares | default list }}
{{- if not (regexMatch $name (toString $middleware)) }}
{{- fail (printf "routes.middlewares: %q is not the name of a Middleware in the release's namespace." (toString $middleware)) }}
{{- end }}
{{- end }}
{{- $backends := $routes.backends | default dict }}
{{- range $service := $known }}
{{- if not (get $backends $service) }}
{{- fail (printf "routes.backends.%s is required: the Service its requests go to, e.g. avalon-api." $service) }}
{{- end }}
{{- end }}
{{- range $service, $backend := $backends }}
{{- if not (has $service $known) }}
{{- fail (printf "routes.backends.%s names no API service: the services are %s." $service (join ", " $known)) }}
{{- end }}
{{- if not (regexMatch $name (toString $backend)) }}
{{- fail (printf "routes.backends.%s is %q, which is not a Service name." $service (toString $backend)) }}
{{- end }}
{{- end }}
{{- $seen := list }}
{{- range $override := $routes.overrides | default list }}
{{- $path := get $override "path" | default "" | toString }}
{{- $service := get $override "service" | default "" | toString }}
{{- $backend := get $override "backend" | default "" | toString }}
{{- if not (regexMatch "^(/[a-z0-9-]+)+$" $path) }}
{{- fail (printf "routes.overrides: the path %q is not written as the manifest writes rules: lower-case segments of letters, digits and '-', each after a '/', with no '/' at the end." $path) }}
{{- end }}
{{- if has $path $seen }}
{{- fail (printf "routes.overrides lists %s twice." $path) }}
{{- end }}
{{- $seen = append $seen $path }}
{{- if not (has $service $known) }}
{{- fail (printf "routes.overrides: %s names the service %q; the services are %s." $path $service (join ", " $known)) }}
{{- end }}
{{- if not (regexMatch $name $backend) }}
{{- fail (printf "routes.overrides: %s needs a backend, the name of the Service to send it to." $path) }}
{{- end }}
{{- range $rule := $manifest.internal }}
{{- if or (eq $path $rule) (hasPrefix (printf "%s/" $rule) $path) (hasPrefix (printf "%s/" $path) $rule) }}
{{- fail (printf "routes.overrides: %s is served only inside the cluster (%s), and no route names it." $path $rule) }}
{{- end }}
{{- end }}
{{- $owner := include "avalon-api.ownerOf" (list $manifest $path) }}
{{- if ne $owner $service }}
{{- fail (printf "routes.overrides: %s lies outside the rules of %s: the route manifest gives it to %s." $path $service $owner) }}
{{- end }}
{{- range $other, $rules := $manifest.services }}
{{- if ne $other $service }}
{{- range $rule := $rules }}
{{- if hasPrefix (printf "%s/" $path) $rule }}
{{- fail (printf "routes.overrides: %s would also take %s, which belongs to %s; override a path inside the rules of %s alone." $path $rule $other $service) }}
{{- end }}
{{- end }}
{{- end }}
{{- end }}
{{- end }}
{{- end }}

{{/*
One route of the IngressRoute (design D7.2): called with a dict of match, priority, middlewares
(names) and backend (a Service, reached on port).
*/}}
{{- define "avalon-api.route" -}}
- kind: Rule
  match: {{ .match | quote }}
  priority: {{ .priority }}
  {{- with .middlewares }}
  middlewares:
    {{- range $middleware := . }}
    - name: {{ $middleware }}
    {{- end }}
  {{- end }}
  services:
    - name: {{ .backend }}
      port: {{ .port }}
{{- end }}

{{/*
The Secret the pod reads its secrets from: the one existingSecret names, or the one
templates/secret.yaml creates.
*/}}
{{- define "avalon-api.secretName" -}}
{{- if .Values.existingSecret }}
{{- .Values.existingSecret }}
{{- else }}
{{- include "avalon-api.fullname" . }}
{{- end }}
{{- end }}

{{/* Match trusted .NET Playtest configuration; disabled profiles may be prepared in advance. */}}
{{- define "avalon-api.validateSteamPlaytest" -}}
{{- $profile := .Values.storeAuthentication.steamPlaytest | default dict }}
{{- if not (kindIs "map" $profile) }}{{ fail "storeAuthentication.steamPlaytest must be a map" }}{{ end }}
{{- if and (hasKey $profile "enabled") (not (kindIs "bool" $profile.enabled)) }}
{{- fail "storeAuthentication.steamPlaytest.enabled must be a boolean" }}
{{- end }}
{{- $configured := get $profile "appId" | default 0 }}
{{- if and (hasKey $profile "appId") (or (kindIs "bool" $profile.appId) (and (kindIs "string" $profile.appId) (not (regexMatch "^[0-9]+$" $profile.appId)))) }}
{{- fail "storeAuthentication.steamPlaytest.appId must be a uint32 integer" }}
{{- end }}
{{- $appId := int64 $configured }}
{{- if or (lt $appId 0) (gt $appId 4294967295) (ne (float64 $configured) (float64 $appId)) (and (ne $appId 0) (eq $appId (int64 .Values.storeAuthentication.steamAppId))) }}
{{- fail "storeAuthentication.steamPlaytest.appId must be a distinct uint32 integer" }}
{{- end }}
{{- if and (hasKey $profile "allowedWorldIds") (not (kindIs "slice" $profile.allowedWorldIds)) }}
{{- fail "storeAuthentication.steamPlaytest.allowedWorldIds must be a list" }}
{{- end }}
{{- $worlds := get $profile "allowedWorldIds" | default list }}
{{- if and $profile.enabled (or (eq $appId 0) (eq (len $worlds) 0)) }}
{{- fail "enabled storeAuthentication.steamPlaytest requires a positive appId and nonempty allowedWorldIds" }}
{{- end }}
{{- $seen := dict }}
{{- range $world := $worlds }}
{{- $id := int64 $world }}
{{- if or (kindIs "bool" $world) (and (kindIs "string" $world) (not (regexMatch "^[0-9]+$" $world))) (le $id 0) (gt $id 65535) (ne (float64 $world) (float64 $id)) (hasKey $seen (toString $id)) }}
{{- fail "storeAuthentication.steamPlaytest.allowedWorldIds requires distinct positive uint16 integers" }}
{{- end }}
{{- $_ := set $seen (toString $id) true }}
{{- end }}
{{- end }}

{{/*
The Secret keys holding one world's connection strings (#523). Called with (list <id> <entry>).
worldKey / charactersKey name them (existingSecret only, trimmed); blank or absent, they default to
database-world-<id>-connection-string and database-characters-<id>-connection-string, which is
also what the chart-managed Secret uses.
*/}}
{{- define "avalon-api.worldKey" -}}
{{- $entry := index . 1 | default dict }}
{{- get $entry "worldKey" | default "" | toString | trim | default (printf "database-world-%s-connection-string" (index . 0)) }}
{{- end }}

{{- define "avalon-api.charactersKey" -}}
{{- $entry := index . 1 | default dict }}
{{- get $entry "charactersKey" | default "" | toString | trim | default (printf "database-characters-%s-connection-string" (index . 0)) }}
{{- end }}

{{/*
Refuses a chart that would start an api with no world, a world id the api refuses, or (with the
chart-managed Secret) a world missing one of its strings; and the removed single-pair values.
Also refuses a Secret key a pod could not use or would share (#523): worldKey and charactersKey
only with existingSecret (the chart-managed Secret names its own keys), every key a valid Secret
key name, and no key read by two settings, the chart's own keys included, since two env vars on
one key would silently read one value. Called with (list <root> <parts>), the parts of each world's
databases the release's services read (avalon-api.worldParts, #794): only those strings and keys
are checked, and a release whose services read no world database ignores worlds.
*/}}
{{- define "avalon-api.validateWorlds" -}}
{{- $root := index . 0 }}
{{- $parts := index . 1 }}
{{- if $parts }}
{{- if or (dig "world" "connectionString" "" $root.Values.database) (dig "characters" "connectionString" "" $root.Values.database) }}
{{- fail "database.world and database.characters were replaced by worlds.<id> (Database:Worlds, #523): move each string to worlds.<id>.world.connectionString and worlds.<id>.characters.connectionString, <id> being the world's id in the auth Worlds table." }}
{{- end }}
{{- if not $root.Values.worlds }}
{{- fail "worlds lists no world: the API needs at least one, keyed by its id in the auth Worlds table. Give worlds.<id>.world.connectionString and worlds.<id>.characters.connectionString (--set-file), or with existingSecret the keys worlds.<id>.worldKey and worlds.<id>.charactersKey." }}
{{- end }}
{{- $used := dict "jwt-signing-private-key" "authentication.signingKey" "game-auth-host-key" "gameAuth.hostKey" "database-auth-connection-string" "database.auth.connectionString" "cache-password" "cache.password" "notification-private-key" "notification.privateKey" "distribution-secret-key" "distribution.secretAccessKey" }}
{{- $cacheKey := include "avalon-api.cachePasswordKey" $root }}
{{- if ne $cacheKey "cache-password" }}{{ $_ := set $used $cacheKey "cache.passwordKey" }}{{ end }}
{{- range $id, $entry := $root.Values.worlds }}
{{- if not (and (regexMatch "^[1-9][0-9]{0,4}$" $id) (le (atoi $id) 65535)) }}
{{- fail (printf "worlds.%s: a world id is a positive integer up to 65535 with no leading zeros, the id of the world's row in the auth Worlds table." $id) }}
{{- end }}
{{- $entry = $entry | default dict }}
{{- if not $root.Values.existingSecret }}
{{- range $field := list "worldKey" "charactersKey" }}
{{- if get $entry $field | default "" | toString | trim }}
{{- fail (printf "worlds.%s.%s names a key of a Secret you manage, so it needs existingSecret: without it the chart creates the Secret and names its keys itself." $id $field) }}
{{- end }}
{{- end }}
{{- range $part := $parts }}
{{- if not (dig $part "connectionString" "" $entry | toString | trim) }}
{{- fail (printf "worlds.%s.%s.connectionString is required unless existingSecret is set." $id $part) }}
{{- end }}
{{- end }}
{{- end }}
{{- range $part := $parts }}
{{- $field := printf "%sKey" $part }}
{{- $key := include (printf "avalon-api.%sKey" $part) (list $id $entry) }}
{{- if not (regexMatch "^[-._a-zA-Z0-9]+$" $key) }}
{{- fail (printf "worlds.%s.%s is %q: a Secret key is letters, digits, '-', '_' and '.' only." $id $field $key) }}
{{- end }}
{{- if hasKey $used $key }}
{{- fail (printf "worlds.%s.%s is %q, the key %s already reads: two settings on one key would read the same value." $id $field $key (get $used $key)) }}
{{- end }}
{{- $_ := set $used $key (printf "worlds.%s.%s" $id $field) }}
{{- end }}
{{- end }}
{{- end }}
{{- end }}
{{- /*
The key of the release's Secret the Redis password is read from (#803): cache.passwordKey, trimmed, or
cache-password when it is left out.
*/}}
{{- define "avalon-api.cachePasswordKey" -}}
{{- .Values.cache.passwordKey | default "" | toString | trim | default "cache-password" }}
{{- end }}

{{- /*
Redis sign-in (#803), checked only where a service reads Application:Cache. cache.username is a Redis
ACL user name, no whitespace. A cache.passwordKey other than cache-password names a key of a Secret you
manage, so it needs existingSecret, and it must be a valid key name that no other setting reads.
*/}}
{{- define "avalon-api.validateCache" -}}
{{- $username := .Values.cache.username | default "" | toString }}
{{- if and $username (not (regexMatch "^[^[:space:]]+$" $username)) }}
{{- fail (printf "cache.username is %q: a Redis user name has no whitespace." $username) }}
{{- end }}
{{- $key := include "avalon-api.cachePasswordKey" . }}
{{- if ne $key "cache-password" }}
{{- if not .Values.existingSecret }}
{{- fail "cache.passwordKey names a key of a Secret you manage, so it needs existingSecret: without it the chart creates the Secret and keeps the password under cache-password." }}
{{- end }}
{{- if not (regexMatch "^[-._a-zA-Z0-9]+$" $key) }}
{{- fail (printf "cache.passwordKey is %q: a Secret key is letters, digits, '-', '_' and '.' only." $key) }}
{{- end }}
{{- $used := dict "jwt-signing-private-key" "authentication.signingKey" "game-auth-host-key" "gameAuth.hostKey" "database-auth-connection-string" "database.auth.connectionString" "notification-private-key" "notification.privateKey" "distribution-secret-key" "distribution.secretAccessKey" "balance-shared-secret" "balance.sharedSecret" }}
{{- if hasKey $used $key }}
{{- fail (printf "cache.passwordKey is %q, the key %s already reads: two settings on one key would read the same value." $key (get $used $key)) }}
{{- end }}
{{- end }}
{{- end }}

{{- define "avalon-api.validateEmail" -}}
{{- $email := .Values.email -}}
{{- if not (has $email.sender (list "None" "Resend")) -}}
{{- fail "email.sender must be None or Resend" -}}
{{- end -}}
{{- range $field := list "verificationCooldownSeconds" "maxVerificationSendsPerAccount" "maxVerificationSendsPerSource" -}}
{{- $value := get $email $field | toString -}}
{{- if or (not (regexMatch "^[1-9][0-9]{0,9}$" $value)) (gt (int64 $value) 2147483647) -}}
{{- fail (printf "email.%s must be a positive int32" $field) -}}
{{- end -}}
{{- end -}}
{{- if eq $email.sender "Resend" -}}
{{- range $field := list "from" "verificationSiteOrigin" "existingSecret" "resendApiKeyKey" -}}
{{- if not (get $email $field | toString | trim) -}}
{{- fail (printf "email.%s is required for Resend" $field) -}}
{{- end -}}
{{- end -}}
{{- if not (regexMatch "^[^@[:space:]<>]+@[^@[:space:]<>]+$" $email.from) -}}
{{- fail "email.from must be a bare email address" -}}
{{- end -}}
{{- if not (regexMatch "^[-._a-zA-Z0-9]+$" $email.resendApiKeyKey) -}}
{{- fail "email.resendApiKeyKey must be a Kubernetes Secret key" -}}
{{- end -}}
{{- end -}}
{{- if and $email.verificationSiteOrigin (not (regexMatch "^https://([A-Za-z0-9.-]+|\\[[a-fA-F0-9:]+\\])(:[0-9]+)?/?$" $email.verificationSiteOrigin)) -}}
{{- fail "email.verificationSiteOrigin must be an HTTPS origin without path, userinfo, query or fragment" -}}
{{- end -}}
{{- end -}}

{{- define "avalon-api.validateCommerce" -}}
{{- $commerce := .Values.commerce -}}
{{- if $commerce.enabled -}}
{{- $isolated := and (eq .Values.environment "Development") (eq .Values.storeAuthentication.environment "development") (eq .Values.storeAuthentication.steamIdentityPrefix "avalon-auth-dev") (eq $commerce.licenseEnvironment "development") -}}
{{- $existingAccounts := and $commerce.allowExistingAccountSandbox (eq .Values.environment "Production") (eq .Values.storeAuthentication.environment "production") (eq $commerce.licenseEnvironment "production") -}}
{{- if or (not (or $isolated $existingAccounts)) (ne $commerce.paymentEnvironment "sandbox") -}}
{{- fail "commerce requires an isolated Development sandbox or explicit existing-account sandbox opt-in; live payments remain disabled" -}}
{{- end -}}
{{- range $field := list "provider" "publicSiteOrigin" "offerId" "providerPriceId" "providerCatalogProductId" "providerAccountId" "existingSecret" "apiKeyKey" "webhookSecretKey" -}}
{{- if not (get $commerce $field | toString | trim) -}}{{- fail (printf "commerce.%s is required" $field) -}}{{- end -}}
{{- end -}}
{{- if or (not (regexMatch "^[1-9][0-9]*$" (toString $commerce.amountMinor))) (ne (int $commerce.quantity) 1) (not (regexMatch "^[a-z]{3}$" $commerce.currency)) (not $commerce.paymentMethods) -}}
{{- fail "commerce requires a positive minor-unit amount, currency, one license and payment methods" -}}
{{- end -}}
{{- if not (regexMatch "^https://([A-Za-z0-9.-]+|\\[[a-fA-F0-9:]+\\])/?$" $commerce.publicSiteOrigin) -}}
{{- fail "commerce.publicSiteOrigin must be a bare HTTPS origin" -}}
{{- end -}}
{{- if or (not (regexMatch "^[-._a-zA-Z0-9]+$" $commerce.apiKeyKey)) (not (regexMatch "^[-._a-zA-Z0-9]+$" $commerce.webhookSecretKey)) (eq $commerce.apiKeyKey $commerce.webhookSecretKey) -}}
{{- fail "commerce secret keys must be valid and distinct" -}}
{{- end -}}
{{- end -}}
{{- end -}}
