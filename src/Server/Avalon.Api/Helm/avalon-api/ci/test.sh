#!/usr/bin/env bash
# Renders the chart the way homelab and the release use it and asserts on the output.
set -euo pipefail
cd "$(dirname "$0")/.."
KEY=$(printf 'k%.0s' $(seq 1 64))
CACHE="--set cache.host=redis:6379"

helm lint . $CACHE --set authentication.issuerSigningKey="$KEY"

out=$(helm template t . $CACHE --set existingSecret=avalon-api)
grep -q "kind: Deployment" <<<"$out"                                   || { echo "not a Deployment"; exit 1; }
! grep -q "kind: StatefulSet" <<<"$out"                                || { echo "still a StatefulSet"; exit 1; }
! grep -q "kind: HorizontalPodAutoscaler" <<<"$out"                    || { echo "HPA on by default"; exit 1; }
grep -q 'image: "ghcr.io/woozchucky/avalon-server/api:0.0.0-dev"' <<<"$out" || { echo "tag must default to appVersion"; exit 1; }
grep -q "path: /alive" <<<"$out" && grep -q "path: /health" <<<"$out"  || { echo "probes missing"; exit 1; }
grep -A1 "Application__MapAssets__ChunkAssetRoot" <<<"$out" | grep -q '/app/Maps' || { echo "map root missing"; exit 1; }
grep -A1 "ASPNETCORE_ENVIRONMENT" <<<"$out" | grep -q 'Production'    || { echo "environment missing"; exit 1; }
! grep -q "kind: Secret" <<<"$out"                                     || { echo "rendered a Secret despite existingSecret"; exit 1; }

hpa=$(helm template t . $CACHE --set existingSecret=x --set autoscaling.enabled=true)
grep -A2 "scaleTargetRef" <<<"$hpa" | grep -q "kind: Deployment"       || { echo "HPA must target the Deployment"; exit 1; }

if helm template t . $CACHE --set existingSecret=x --set cache.password=leak >/dev/null 2>&1; then
  echo "existingSecret + inline secret must fail"; exit 1
fi
ot=$(helm template t . $CACHE --set existingSecret=x --set otel.endpoint=http://otel-collector:4317 --set 'otel.resourceAttributes.deployment\.environment=production')
grep -A1 "name: OTEL_EXPORTER_OTLP_ENDPOINT" <<<"$ot" | grep -q "http://otel-collector:4317" || { echo "otel endpoint missing"; exit 1; }
grep -A1 "name: OTEL_SERVICE_NAME" <<<"$ot" | grep -q '"t-avalon-api"'                                  || { echo "service name must default to the fullname"; exit 1; }
grep -A1 "name: OTEL_RESOURCE_ATTRIBUTES" <<<"$ot" | grep -q '"deployment.environment=production"'          || { echo "resource attributes missing"; exit 1; }
! grep -q "OTEL_" <<<"$out"                                                                              || { echo "otel env rendered without an endpoint"; exit 1; }
grep -A1 "name: Application__Cache__Host" <<<"$out" | grep -q '"redis:6379"'              || { echo "cache host missing"; exit 1; }
if helm template t . --set existingSecret=x >/dev/null 2>&1; then
  echo "rendering without cache.host must fail"; exit 1
fi
! grep -q "Application__Distribution__" <<<"$out"                                              || { echo "distribution env rendered without an endpoint"; exit 1; }
dist=$(helm template t . $CACHE --set existingSecret=x --set distribution.endpoint=http://garage.distribution.svc:3900 --set distribution.publicUrl=https://dist.example --set distribution.accessKeyId=GKabc)
grep -A1 "name: Application__Distribution__Endpoint" <<<"$dist" | grep -q "http://garage.distribution.svc:3900" || { echo "distribution endpoint missing"; exit 1; }
grep -A1 "name: Application__Distribution__PublicUrl" <<<"$dist" | grep -q "https://dist.example"                || { echo "distribution public url missing"; exit 1; }
grep -A1 "name: Application__Distribution__Bucket" <<<"$dist" | grep -q '"avalon-dist"'                            || { echo "distribution bucket missing"; exit 1; }
grep -A1 "name: Application__Distribution__Region" <<<"$dist" | grep -q '"garage"'                                 || { echo "distribution region missing"; exit 1; }
grep -A1 "name: Application__Distribution__AccessKeyId" <<<"$dist" | grep -q '"GKabc"'                             || { echo "distribution key id missing"; exit 1; }
grep -A4 "name: Application__Distribution__SecretAccessKey" <<<"$dist" | grep -q "key: distribution-secret-key"    || { echo "distribution secret must come from the Secret"; exit 1; }
if helm template t . $CACHE --set existingSecret=x --set distribution.secretAccessKey=leak >/dev/null 2>&1; then
  echo "existingSecret + inline distribution.secretAccessKey must fail"; exit 1
fi
echo "avalon-api chart OK"
