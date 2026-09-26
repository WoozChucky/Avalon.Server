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

pp=$(helm template t . $CACHE --set existingSecret=x --set server.proxyProtocol.enabled=true   --set 'server.proxyProtocol.trustedProxies[0]=10.42.0.0/16' --set 'service.loadBalancerSourceRanges[0]=10.10.1.17/32')
grep -A1 "name: Hosting__ProxyProtocol__Enabled" <<<"$pp" | grep -q '"true"'            || { echo "proxy protocol not enabled"; exit 1; }
grep -A1 "name: Hosting__ProxyProtocol__TrustedProxies__0" <<<"$pp" | grep -q "10.42.0.0/16" || { echo "trusted proxies missing"; exit 1; }
grep -A1 "loadBalancerSourceRanges:" <<<"$pp" | grep -q "10.10.1.17/32"                 || { echo "source ranges missing"; exit 1; }
off=$(helm template t . $CACHE --set existingSecret=x)
! grep -q "Hosting__ProxyProtocol__TrustedProxies" <<<"$off"                            || { echo "trusted proxies rendered when unset"; exit 1; }
! grep -q "loadBalancerSourceRanges" <<<"$off"                                          || { echo "source ranges rendered when unset"; exit 1; }

if helm template t . $CACHE --set existingSecret=x --set server.cache.password=leak >/dev/null 2>&1; then
  echo "existingSecret + inline secret must fail"; exit 1
fi
echo "avalon-world chart OK"
