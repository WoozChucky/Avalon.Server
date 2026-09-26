#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
PFX=$(printf 'not-a-real-pfx' | base64)

CACHE="--set server.cache.host=redis:6379"

helm lint . $CACHE --set server.tls.pfx="$PFX" --set server.tls.password=p --set server.database.auth.connectionString=Host=db

out=$(helm template t . $CACHE --set existingSecret=avalon-auth)
grep -q 'image: "ghcr.io/woozchucky/avalon-server/auth:0.0.0-dev"' <<<"$out" || { echo "tag must default to appVersion"; exit 1; }
grep -q "secretKeyRef" <<<"$out"                                          || { echo "secrets must come from a Secret"; exit 1; }
! grep -A1 "name: Cache__Password" <<<"$out" | grep -q "value:"           || { echo "cache password inline"; exit 1; }
grep -A1 "Hosting__Security__CertificatePath" <<<"$out" | grep -q "/app/certs/tls.pfx" || { echo "cert path missing"; exit 1; }
grep -q "mountPath: /app/certs" <<<"$out"                                 || { echo "cert not mounted"; exit 1; }
grep -q "tcpSocket" <<<"$out"                                             || { echo "tcp probes missing"; exit 1; }
grep -q "startupProbe:" <<<"$out"                                         || { echo "startupProbe missing"; exit 1; }
! grep -q "Database__Characters" <<<"$out"                                || { echo "auth only uses the auth db"; exit 1; }

lb=$(helm template t . $CACHE --set existingSecret=x --set service.type=LoadBalancer)
grep -q "type: LoadBalancer" <<<"$lb"                                     || { echo "service.type ignored"; exit 1; }

if helm template t . $CACHE --set server.database.auth.connectionString=Host=db >/dev/null 2>&1; then
  echo "rendering without a certificate must fail"; exit 1
fi
if helm template t . --set existingSecret=x >/dev/null 2>&1; then
  echo "rendering without server.cache.host must fail"; exit 1
fi
if helm template t . $CACHE --set server.tls.pfx="$PFX" >/dev/null 2>&1; then
  echo "rendering the chart Secret without a connection string must fail"; exit 1
fi
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
ot=$(helm template t . $CACHE --set existingSecret=x --set otel.endpoint=http://otel-collector:4317 --set 'otel.resourceAttributes.deployment\.environment=production')
grep -A1 "name: OTEL_EXPORTER_OTLP_ENDPOINT" <<<"$ot" | grep -q "http://otel-collector:4317" || { echo "otel endpoint missing"; exit 1; }
grep -A1 "name: OTEL_SERVICE_NAME" <<<"$ot" | grep -q '"t-avalon-auth"'                                 || { echo "service name must default to the fullname"; exit 1; }
grep -A1 "name: OTEL_RESOURCE_ATTRIBUTES" <<<"$ot" | grep -q '"deployment.environment=production"'          || { echo "resource attributes missing"; exit 1; }
named=$(helm template t . $CACHE --set existingSecret=x --set otel.endpoint=http://c:4317 --set otel.serviceName=custom)
grep -A1 "name: OTEL_SERVICE_NAME" <<<"$named" | grep -q '"custom"'                                      || { echo "otel.serviceName ignored"; exit 1; }
! grep -q "OTEL_RESOURCE_ATTRIBUTES" <<<"$named"                                                         || { echo "empty resource attributes rendered"; exit 1; }
! grep -q "OTEL_" <<<"$off"                                                                              || { echo "otel env rendered without an endpoint"; exit 1; }
echo "avalon-auth chart OK"
