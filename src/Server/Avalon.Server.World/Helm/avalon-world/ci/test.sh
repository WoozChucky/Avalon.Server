#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

CACHE="--set server.cache.host=redis:6379"
DBS="--set server.database.auth.connectionString=Host=db --set server.database.characters.connectionString=Host=db --set server.database.world.connectionString=Host=db"

helm lint . $CACHE $DBS

out=$(helm template t . $CACHE --set existingSecret=avalon-world)
grep -q 'image: "ghcr.io/woozchucky/avalon-server/world:0.0.0-dev"' <<<"$out" || { echo "tag must default to appVersion"; exit 1; }
grep -q "name: Game__MaxCharactersPerAccount" <<<"$out"                     || { echo "MaxCharactersPerAccount missing"; exit 1; }
! grep -q "MaxPlayersPerAccount" <<<"$out"                                  || { echo "stale MaxPlayersPerAccount key"; exit 1; }
for k in Database__Auth Database__Characters Database__World Cache__Password; do
  grep -A1 "name: ${k}" <<<"$out" | grep -q "valueFrom"                     || { echo "$k must come from the Secret"; exit 1; }
done
grep -q "tcpSocket" <<<"$out"                                               || { echo "tcp probes missing"; exit 1; }
grep -q "startupProbe:" <<<"$out"                                           || { echo "startupProbe missing"; exit 1; }

if helm template t . --set existingSecret=x >/dev/null 2>&1; then
  echo "rendering without server.cache.host must fail"; exit 1
fi
if helm template t . $CACHE >/dev/null 2>&1; then
  echo "rendering the chart Secret without connection strings must fail"; exit 1
fi

lb=$(helm template t . $CACHE --set existingSecret=x --set service.type=LoadBalancer)
grep -q "type: LoadBalancer" <<<"$lb"                                       || { echo "service.type ignored"; exit 1; }

if helm template t . $CACHE --set existingSecret=x --set server.cache.password=leak >/dev/null 2>&1; then
  echo "existingSecret + inline secret must fail"; exit 1
fi
echo "avalon-world chart OK"
