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
one key would silently read one value.
*/}}
{{- define "avalon-api.validateWorlds" -}}
{{- if or (dig "world" "connectionString" "" .Values.database) (dig "characters" "connectionString" "" .Values.database) }}
{{- fail "database.world and database.characters were replaced by worlds.<id> (Database:Worlds, #523): move each string to worlds.<id>.world.connectionString and worlds.<id>.characters.connectionString, <id> being the world's id in the auth Worlds table." }}
{{- end }}
{{- if not .Values.worlds }}
{{- fail "worlds lists no world: the API needs at least one, keyed by its id in the auth Worlds table. Give worlds.<id>.world.connectionString and worlds.<id>.characters.connectionString (--set-file), or with existingSecret the keys worlds.<id>.worldKey and worlds.<id>.charactersKey." }}
{{- end }}
{{- $used := dict "jwt-signing-key" "authentication.issuerSigningKey" "database-auth-connection-string" "database.auth.connectionString" "cache-password" "cache.password" "notification-private-key" "notification.privateKey" "distribution-secret-key" "distribution.secretAccessKey" }}
{{- range $id, $entry := .Values.worlds }}
{{- if not (and (regexMatch "^[1-9][0-9]{0,4}$" $id) (le (atoi $id) 65535)) }}
{{- fail (printf "worlds.%s: a world id is a positive integer up to 65535 with no leading zeros, the id of the world's row in the auth Worlds table." $id) }}
{{- end }}
{{- $entry = $entry | default dict }}
{{- if not $.Values.existingSecret }}
{{- range $field := list "worldKey" "charactersKey" }}
{{- if get $entry $field | default "" | toString | trim }}
{{- fail (printf "worlds.%s.%s names a key of a Secret you manage, so it needs existingSecret: without it the chart creates the Secret and names its keys itself." $id $field) }}
{{- end }}
{{- end }}
{{- if not (dig "world" "connectionString" "" $entry | toString | trim) }}
{{- fail (printf "worlds.%s.world.connectionString is required unless existingSecret is set." $id) }}
{{- end }}
{{- if not (dig "characters" "connectionString" "" $entry | toString | trim) }}
{{- fail (printf "worlds.%s.characters.connectionString is required unless existingSecret is set." $id) }}
{{- end }}
{{- end }}
{{- range $pair := list (list "worldKey" (include "avalon-api.worldKey" (list $id $entry))) (list "charactersKey" (include "avalon-api.charactersKey" (list $id $entry))) }}
{{- $field := index $pair 0 }}
{{- $key := index $pair 1 }}
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
{{- define "avalon-api.validateEmail" -}}
{{- $email := .Values.email -}}
{{- if hasKey $email "resendApiKey" -}}
{{- fail "email.resendApiKey is forbidden; use email.existingSecret and email.resendApiKeyKey" -}}
{{- end -}}
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
{{- if hasKey $commerce "apiKey" -}}{{- fail "commerce.apiKey is forbidden; use an existing Secret reference" -}}{{- end -}}
{{- if hasKey $commerce "webhookSecret" -}}{{- fail "commerce.webhookSecret is forbidden; use an existing Secret reference" -}}{{- end -}}
{{- if $commerce.enabled -}}
{{- if or (ne .Values.environment "Development") (ne .Values.storeAuthentication.environment "development") (ne .Values.storeAuthentication.steamIdentityPrefix "avalon-auth-dev") (ne $commerce.paymentEnvironment "sandbox") (ne $commerce.licenseEnvironment "development") -}}
{{- fail "commerce requires an isolated Development sandbox with development store authentication" -}}
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
