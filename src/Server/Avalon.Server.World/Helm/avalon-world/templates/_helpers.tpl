{{/*
Expand the name of the chart.
*/}}
{{- define "avalon-server.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Create a default fully qualified app name.
We truncate at 63 chars because some Kubernetes name fields are limited to this (by the DNS naming spec).
If release name contains chart name it will be used as a full name.
*/}}
{{- define "avalon-server.fullname" -}}
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
{{- define "avalon-server.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Common labels
*/}}
{{- define "avalon-server.labels" -}}
helm.sh/chart: {{ include "avalon-server.chart" . }}
{{ include "avalon-server.selectorLabels" . }}
{{- if .Chart.AppVersion }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
{{- end }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{/*
Selector labels
*/}}
{{- define "avalon-server.selectorLabels" -}}
app.kubernetes.io/name: {{ include "avalon-server.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}

{{/*
The Secret the pod reads its secrets from: the one existingSecret names, or the one
templates/secret.yaml creates.
*/}}
{{- define "avalon-server.secretName" -}}
{{- if .Values.existingSecret }}
{{- .Values.existingSecret }}
{{- else }}
{{- include "avalon-server.fullname" . }}
{{- end }}
{{- end }}

{{/*
The restart drain (#768), in whole seconds. Refuses what the world would refuse at start (World:Shutdown):
a fraction, a drain outside 0..3600 and a margin outside 21..3600. The margin's floor is the world's
WorldShutdownConfiguration.MinimumSaveMargin: its 20 s save wait plus the drain's 1 s backstop.
*/}}
{{- define "avalon-server.drainSeconds" -}}
{{- $s := .Values.shutdown.drainSeconds -}}
{{- if or (lt (float64 $s) 0.0) (gt (float64 $s) 3600.0) (ne (float64 $s) (float64 (int64 $s))) -}}
{{- fail "shutdown.drainSeconds must be a whole number of seconds from 0 to 3600" -}}
{{- end -}}
{{- int64 $s -}}
{{- end }}

{{- define "avalon-server.saveMarginSeconds" -}}
{{- $s := .Values.shutdown.saveMarginSeconds -}}
{{- if or (lt (float64 $s) 21.0) (gt (float64 $s) 3600.0) (ne (float64 $s) (float64 (int64 $s))) -}}
{{- fail "shutdown.saveMarginSeconds must be a whole number of seconds from 21 to 3600" -}}
{{- end -}}
{{- int64 $s -}}
{{- end }}

{{/*
Whole seconds as a .NET TimeSpan, d.hh:mm:ss: a bare number would bind as days.
*/}}
{{- define "avalon-server.timeSpan" -}}
{{- $s := int64 . -}}
{{- printf "%d.%02d:%02d:%02d" (div $s 86400) (div (mod $s 86400) 3600) (div (mod $s 3600) 60) (mod $s 60) -}}
{{- end }}
