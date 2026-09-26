#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
PFX=$(printf 'not-a-real-pfx' | base64)

helm lint . --set server.tls.pfx="$PFX" --set server.tls.password=p

out=$(helm template t . --set existingSecret=avalon-auth)
grep -q 'image: "ghcr.io/woozchucky/avalon-server/auth:0.0.0-dev"' <<<"$out" || { echo "tag must default to appVersion"; exit 1; }
grep -q "secretKeyRef" <<<"$out"                                          || { echo "secrets must come from a Secret"; exit 1; }
! grep -A1 "name: Cache__Password" <<<"$out" | grep -q "value:"           || { echo "cache password inline"; exit 1; }
grep -A1 "Hosting__Security__CertificatePath" <<<"$out" | grep -q "/app/certs/tls.pfx" || { echo "cert path missing"; exit 1; }
grep -q "mountPath: /app/certs" <<<"$out"                                 || { echo "cert not mounted"; exit 1; }
grep -q "tcpSocket" <<<"$out"                                             || { echo "tcp probes missing"; exit 1; }
! grep -q "Database__Characters" <<<"$out"                                || { echo "auth only uses the auth db"; exit 1; }

lb=$(helm template t . --set existingSecret=x --set service.type=LoadBalancer)
grep -q "type: LoadBalancer" <<<"$lb"                                     || { echo "service.type ignored"; exit 1; }

if helm template t . >/dev/null 2>&1; then
  echo "rendering without a certificate must fail"; exit 1
fi
if helm template t . --set existingSecret=x --set server.cache.password=leak >/dev/null 2>&1; then
  echo "existingSecret + inline secret must fail"; exit 1
fi
echo "avalon-auth chart OK"
