#!/usr/bin/env bash
# Renders the chart the way homelab and the release use it and asserts on the output.
set -euo pipefail
cd "$(dirname "$0")/.."
KEY=$(printf 'k%.0s' $(seq 1 64))

helm lint . --set authentication.issuerSigningKey="$KEY"

out=$(helm template t . --set existingSecret=avalon-api)
grep -q "kind: Deployment" <<<"$out"                                   || { echo "not a Deployment"; exit 1; }
! grep -q "kind: StatefulSet" <<<"$out"                                || { echo "still a StatefulSet"; exit 1; }
! grep -q "kind: HorizontalPodAutoscaler" <<<"$out"                    || { echo "HPA on by default"; exit 1; }
grep -q 'image: "ghcr.io/woozchucky/avalon-server/api:0.0.0-dev"' <<<"$out" || { echo "tag must default to appVersion"; exit 1; }
grep -q "path: /alive" <<<"$out" && grep -q "path: /health" <<<"$out"  || { echo "probes missing"; exit 1; }
grep -A1 "Application__MapAssets__ChunkAssetRoot" <<<"$out" | grep -q '/app/Maps' || { echo "map root missing"; exit 1; }
grep -A1 "ASPNETCORE_ENVIRONMENT" <<<"$out" | grep -q 'Production'    || { echo "environment missing"; exit 1; }
! grep -q "kind: Secret" <<<"$out"                                     || { echo "rendered a Secret despite existingSecret"; exit 1; }

hpa=$(helm template t . --set existingSecret=x --set autoscaling.enabled=true)
grep -A2 "scaleTargetRef" <<<"$hpa" | grep -q "kind: Deployment"       || { echo "HPA must target the Deployment"; exit 1; }

if helm template t . --set existingSecret=x --set cache.password=leak >/dev/null 2>&1; then
  echo "existingSecret + inline secret must fail"; exit 1
fi
echo "avalon-api chart OK"
