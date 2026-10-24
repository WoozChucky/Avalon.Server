{{- define "avalon-crashes.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}
{{- define "avalon-crashes.fullname" -}}
{{- default (printf "%s-%s" .Release.Name (include "avalon-crashes.name" .)) .Values.fullnameOverride | trunc 54 | trimSuffix "-" -}}
{{- end -}}
{{- define "avalon-crashes.selectorLabels" -}}
app.kubernetes.io/name: {{ include "avalon-crashes.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}
{{- define "avalon-crashes.podSecurity" -}}
runAsNonRoot: true
runAsUser: 1654
runAsGroup: 1654
fsGroup: 1654
seccompProfile:
  type: RuntimeDefault
{{- end -}}
{{- define "avalon-crashes.containerSecurity" -}}
allowPrivilegeEscalation: false
readOnlyRootFilesystem: true
capabilities:
  drop: [ALL]
{{- end -}}
{{- define "avalon-crashes.resources" -}}
requests:
  cpu: 100m
  memory: 256Mi
  ephemeral-storage: 6Gi
limits:
  cpu: "1"
  memory: 1Gi
  ephemeral-storage: 7Gi
{{- end -}}
{{- define "avalon-crashes.validate" -}}
{{- if ne (toString .Values.replicaCount) "1" }}{{ fail "replicaCount must be 1 for the first release" }}{{ end -}}
{{- if ne (toString .Values.retentionDays) "90" }}{{ fail "retentionDays is fixed at 90; runtime never extends retention" }}{{ end -}}
{{- if not .Values.existingSecret }}{{ fail "existingSecret must name the isolated worker Secret" }}{{ end -}}
{{- if not (regexMatch "^https://[^/@?#]+(:[0-9]+)?(/[^?#]*)?$" .Values.storage.endpoint) }}{{ fail "storage.endpoint must be HTTPS without credentials, query or fragment" }}{{ end -}}
{{- if not .Values.storage.region }}{{ fail "storage.region is required" }}{{ end -}}
{{- if or (ne .Values.storage.bucket "crashes") (ne .Values.symbols.originalBucket "symbols") (ne .Values.symbols.convertedBucket "symbols-sym") }}{{ fail "buckets must remain crashes, symbols and symbols-sym with distinct scoped roles" }}{{ end -}}
{{- if .Values.networkPolicy.enabled -}}
{{- if or (not .Values.networkPolicy.database.peers) (not .Values.networkPolicy.garage.peers) (not .Values.networkPolicy.dnsPeers) (not .Values.networkPolicy.healthPeers) }}{{ fail "networkPolicy requires explicit database, garage, DNS and health peers" }}{{ end -}}
{{- if and .Values.otel.endpoint (not .Values.otel.peers) }}{{ fail "otel.peers required with OTLP and NetworkPolicy" }}{{ end -}}
{{- end -}}
{{- end -}}
