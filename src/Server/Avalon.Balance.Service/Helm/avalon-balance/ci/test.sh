#!/usr/bin/env bash
# Renders the chart the way homelab and the release use it and asserts on the output.
set -euo pipefail
cd "$(dirname "$0")/.."
SECRET=$(printf 's%.0s' $(seq 1 40))

helm lint . --set sharedSecret="$SECRET"

out=$(helm template t . --set existingSecret=avalon-balance)
grep -q "kind: Deployment" <<<"$out"                                        || { echo "not a Deployment"; exit 1; }
grep -q "kind: Service" <<<"$out" && grep -q "type: ClusterIP" <<<"$out"    || { echo "not a ClusterIP Service"; exit 1; }
grep -q 'image: "ghcr.io/woozchucky/avalon-server/balance:0.0.0-dev"' <<<"$out" || { echo "tag must default to appVersion"; exit 1; }
grep -q "replicas: 1$" <<<"$out"                                            || { echo "must run one replica (the queue is in memory)"; exit 1; }
! grep -q "kind: Secret" <<<"$out"                                          || { echo "rendered a Secret despite existingSecret"; exit 1; }
! grep -q "kind: Ingress" <<<"$out"                                         || { echo "rendered an ingress"; exit 1; }
! grep -q "kind: HorizontalPodAutoscaler" <<<"$out"                         || { echo "rendered an HPA"; exit 1; }
grep -A1 "livenessProbe" <<<"$out" | grep -q "httpGet"                      || { echo "liveness probe missing"; exit 1; }
grep -A2 "livenessProbe" <<<"$out" | grep -q "path: /alive"                 || { echo "liveness must use /alive"; exit 1; }
grep -A2 "readinessProbe" <<<"$out" | grep -q "path: /health"               || { echo "readiness must use /health"; exit 1; }
grep -A4 "name: Balance__SharedSecret" <<<"$out" | grep -q "key: balance-shared-secret" || { echo "shared secret must come from the Secret"; exit 1; }
grep -A4 "name: Balance__SharedSecret" <<<"$out" | grep -q "name: avalon-balance"       || { echo "shared secret must come from existingSecret"; exit 1; }
grep -A5 "name: Balance__GitHubToken" <<<"$out" | grep -q "key: github-token"           || { echo "github token must come from the Secret"; exit 1; }
grep -A5 "name: Balance__GitHubToken" <<<"$out" | grep -q "optional: true"              || { echo "github-token must be optional"; exit 1; }
grep -A1 "name: Balance__Repository" <<<"$out" | grep -q '"WoozChucky/Avalon.Server"'   || { echo "repository missing"; exit 1; }
grep -A1 "ASPNETCORE_ENVIRONMENT" <<<"$out" | grep -q 'Production'          || { echo "environment missing"; exit 1; }
grep -A8 "resources:" <<<"$out" | grep -q "cpu: 2"                          || { echo "cpu limit missing"; exit 1; }
grep -A8 "resources:" <<<"$out" | grep -q "memory: 1Gi"                     || { echo "memory limit missing"; exit 1; }
grep -A8 "resources:" <<<"$out" | grep -q "requests:"                       || { echo "resource requests missing"; exit 1; }
! grep -q "OTEL_" <<<"$out"                                                 || { echo "otel env rendered without an endpoint"; exit 1; }

# Chart-managed Secret: both values reach it, the token only when given, and a change restarts the pod.
sec=$(helm template t . --set sharedSecret="$SECRET")
grep -q "kind: Secret" <<<"$sec"                                            || { echo "no Secret rendered"; exit 1; }
grep -q "balance-shared-secret: \"$SECRET\"" <<<"$sec"                      || { echo "shared secret missing from the Secret"; exit 1; }
! grep -q "github-token:" <<<"$sec"                                         || { echo "an unset token must not render"; exit 1; }
grep -q "github-token: \"ghp_x\"" <<<"$(helm template t . --set sharedSecret="$SECRET" --set githubToken=ghp_x)" || { echo "token missing from the Secret"; exit 1; }
a=$(helm template t . --set sharedSecret="$SECRET" | grep "checksum/secret:")
b=$(helm template t . --set sharedSecret="${SECRET}x" | grep "checksum/secret:")
[ -n "$a" ] && [ "$a" != "$b" ]                                             || { echo "a changed secret must change the checksum"; exit 1; }
# No secret value is a default.
! grep -q "$SECRET" <<<"$out"                                               || { echo "secret leaked into the rendered output"; exit 1; }

must_fail() { local why=$1; shift; if helm template t . "$@" >/dev/null 2>&1; then echo "$why"; exit 1; fi; }
must_fail "no secret at all must fail"                       
must_fail "a short shared secret must fail"                  --set sharedSecret=short
must_fail "existingSecret + inline sharedSecret must fail"   --set existingSecret=x --set sharedSecret="$SECRET"
must_fail "existingSecret + inline githubToken must fail"    --set existingSecret=x --set githubToken=leak

ot=$(helm template t . --set existingSecret=x --set otel.endpoint=http://otel-collector:4317 --set 'otel.resourceAttributes.deployment\.environment=production')
grep -A1 "name: OTEL_EXPORTER_OTLP_ENDPOINT" <<<"$ot" | grep -q "http://otel-collector:4317" || { echo "otel endpoint missing"; exit 1; }
grep -A1 "name: OTEL_SERVICE_NAME" <<<"$ot" | grep -q '"t-avalon-balance"'                   || { echo "service name must default to the fullname"; exit 1; }
grep -A1 "name: OTEL_RESOURCE_ATTRIBUTES" <<<"$ot" | grep -q '"deployment.environment=production"' || { echo "resource attributes missing"; exit 1; }
echo "avalon-balance chart OK"
