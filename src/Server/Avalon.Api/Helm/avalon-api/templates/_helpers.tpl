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
{{- $used := dict "jwt-signing-key" "authentication.issuerSigningKey" "database-auth-connection-string" "database.auth.connectionString" "cache-password" "cache.password" "notification-private-key" "notification.privateKey" }}
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
