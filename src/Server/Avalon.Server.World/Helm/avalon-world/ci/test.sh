#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

# Supply mandatory authentication settings for renders and unrelated refusal checks.
AUTHENTICATION=(--values ci/authentication-values.yaml)

CACHE="--set server.cache.host=redis:6379"
DBS="--set server.database.auth.connectionString=Host=db --set server.database.characters.connectionString=Host=db --set server.database.world.connectionString=Host=db"

helm lint . "${AUTHENTICATION[@]}" $CACHE $DBS

out=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=avalon-world)
grep -q 'image: "ghcr.io/woozchucky/avalon-server/world:0.0.0-dev"' <<<"$out" || { echo "tag must default to appVersion"; exit 1; }
grep -q "name: Game__MaxCharactersPerAccount" <<<"$out"                     || { echo "MaxCharactersPerAccount missing"; exit 1; }
! grep -q "MaxPlayersPerAccount" <<<"$out"                                  || { echo "stale MaxPlayersPerAccount key"; exit 1; }
for k in Database__Auth Database__Characters Database__World Cache__Password; do
  grep -A1 "name: ${k}" <<<"$out" | grep -q "valueFrom"                     || { echo "$k must come from the Secret"; exit 1; }
done
grep -q "tcpSocket" <<<"$out"                                               || { echo "tcp probes missing"; exit 1; }
grep -q "startupProbe:" <<<"$out"                                           || { echo "startupProbe missing"; exit 1; }

if helm template t . "${AUTHENTICATION[@]}" --set existingSecret=x >/dev/null 2>&1; then
  echo "rendering without server.cache.host must fail"; exit 1
fi
if helm template t . "${AUTHENTICATION[@]}" $CACHE >/dev/null 2>&1; then
  echo "rendering the chart Secret without connection strings must fail"; exit 1
fi

lb=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set service.type=LoadBalancer)
grep -q "type: LoadBalancer" <<<"$lb"                                       || { echo "service.type ignored"; exit 1; }
# A LoadBalancer allocates no NodePort (#1052): one would reach the pod from the trusted pod network past the source ranges.
grep -q "allocateLoadBalancerNodePorts: false" <<<"$lb"                     || { echo "a LoadBalancer must not allocate a NodePort"; exit 1; }
! grep -q "allocateLoadBalancerNodePorts" <<<"$out"                         || { echo "allocateLoadBalancerNodePorts rendered on a ClusterIP Service"; exit 1; }

pp=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set service.type=LoadBalancer --set server.proxyProtocol.enabled=true   --set 'server.proxyProtocol.trustedProxies[0]=10.42.0.0/16' --set 'service.loadBalancerSourceRanges[0]=10.10.1.17/32')
grep -A1 "name: Hosting__ProxyProtocol__Enabled" <<<"$pp" | grep -q '"true"'            || { echo "proxy protocol not enabled"; exit 1; }
grep -A1 "name: Hosting__ProxyProtocol__TrustedProxies__0" <<<"$pp" | grep -q "10.42.0.0/16" || { echo "trusted proxies missing"; exit 1; }
grep -A1 "loadBalancerSourceRanges:" <<<"$pp" | grep -q "10.10.1.17/32"                 || { echo "source ranges missing"; exit 1; }
off=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x)
! grep -q "Hosting__ProxyProtocol__TrustedProxies" <<<"$off"                            || { echo "trusted proxies rendered when unset"; exit 1; }
! grep -q "loadBalancerSourceRanges" <<<"$off"                                          || { echo "source ranges rendered when unset"; exit 1; }

if helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set server.cache.password=leak >/dev/null 2>&1; then
  echo "existingSecret + inline secret must fail"; exit 1
fi
ot=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set server.game.worldId=2 --set otel.endpoint=http://otel-collector:4317 --set 'otel.resourceAttributes.deployment\.environment=production')
grep -A1 "name: OTEL_EXPORTER_OTLP_ENDPOINT" <<<"$ot" | grep -q "http://otel-collector:4317"      || { echo "otel endpoint missing"; exit 1; }
grep -A1 "name: OTEL_SERVICE_NAME" <<<"$ot" | grep -q '"t-avalon-world"'                                      || { echo "service name must default to the fullname"; exit 1; }
grep -A1 "name: OTEL_RESOURCE_ATTRIBUTES" <<<"$ot" | grep -q '"avalon.world.id=2,deployment.environment=production"' || { echo "world id must lead the resource attributes"; exit 1; }
bare=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set otel.endpoint=http://c:4317)
grep -A1 "name: OTEL_RESOURCE_ATTRIBUTES" <<<"$bare" | grep -q '"avalon.world.id=1"'                         || { echo "world id attribute missing"; exit 1; }
! grep -q "OTEL_" <<<"$off"                                                                                   || { echo "otel env rendered without an endpoint"; exit 1; }
dup=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set server.game.worldId=2 --set otel.endpoint=http://c:4317 --set 'otel.resourceAttributes.avalon\.world\.id=9' --set 'otel.resourceAttributes.deployment\.environment=production')
grep -A1 "name: OTEL_RESOURCE_ATTRIBUTES" <<<"$dup" | grep -q '"avalon.world.id=2,deployment.environment=production"' || { echo "avalon.world.id must come from server.game.worldId, once"; exit 1; }
# Metric export interval: unset leaves the SDK default (60 s); a value renders as milliseconds; a non-positive or non-integer one fails.
! grep -q "OTEL_METRIC_EXPORT_INTERVAL" <<<"$ot"                                                              || { echo "metric export interval rendered when unset"; exit 1; }
mi=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set otel.endpoint=http://c:4317 --set otel.metricExportIntervalMs=10000)
grep -A1 "name: OTEL_METRIC_EXPORT_INTERVAL" <<<"$mi" | grep -q '"10000"'                                      || { echo "metric export interval must render"; exit 1; }
# A values file reads a number as a float; it must still render as digits, not 1e+04.
for n in 10000 1000000; do
  mj=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set otel.endpoint=http://c:4317 --set-json "otel.metricExportIntervalMs=$n") || { echo "a float metric export interval $n must render"; exit 1; }
  grep -A1 "name: OTEL_METRIC_EXPORT_INTERVAL" <<<"$mj" | grep -q "\"$n\""                                  || { echo "a float metric export interval $n must render as digits"; exit 1; }
done
for bad in "otel.metricExportIntervalMs=0" "otel.metricExportIntervalMs=-1" "otel.metricExportIntervalMs=1.5" "otel.metricExportIntervalMs=10s"; do
  if helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set otel.endpoint=http://c:4317 --set "$bad" >/dev/null 2>&1; then
    echo "rendering with $bad must fail"; exit 1
  fi
done
sd=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set shutdown.drainSeconds=300 --set shutdown.saveMarginSeconds=90)
grep -A1 "name: World__Shutdown__DrainTime" <<<"$sd" | grep -q '"0.00:05:00"'                  || { echo "drain must render as a TimeSpan"; exit 1; }
grep -A1 "name: World__Shutdown__SaveMargin" <<<"$sd" | grep -q '"0.00:01:30"'                 || { echo "save margin must render as a TimeSpan"; exit 1; }
grep -q "terminationGracePeriodSeconds: 405" <<<"$sd"                                          || { echo "grace period must be drain + margin + 15"; exit 1; }
grep -A1 "name: World__Shutdown__DrainTime" <<<"$off" | grep -q '"0.00:00:00"'                 || { echo "drain must default to 0"; exit 1; }
grep -q "terminationGracePeriodSeconds: 75" <<<"$off"                                          || { echo "default grace period must be 0 + 60 + 15"; exit 1; }
for bad in "shutdown.drainSeconds=-1" "shutdown.drainSeconds=1.5" "shutdown.drainSeconds=3601" "shutdown.saveMarginSeconds=0" "shutdown.saveMarginSeconds=20"; do
  if helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set "$bad" >/dev/null 2>&1; then
    echo "rendering with $bad must fail"; exit 1
  fi
done
helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set shutdown.saveMarginSeconds=21 >/dev/null || { echo "a 21 s margin must render"; exit 1; }

# Abandoned dungeon lifetime: unset leaves the world's default (15); 0 renders as "0" (a falsy value still renders); a
# values-file float renders as digits; a negative or non-integer one fails.
! grep -q "Game__AbandonedInstanceLifetimeMinutes" <<<"$off"                                    || { echo "abandoned instance lifetime rendered when unset"; exit 1; }
al=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set server.game.abandonedInstanceLifetimeMinutes=0)
grep -A1 "name: Game__AbandonedInstanceLifetimeMinutes" <<<"$al" | grep -q '"0"'                || { echo "an abandoned instance lifetime of 0 must render"; exit 1; }
aj=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set-json server.game.abandonedInstanceLifetimeMinutes=30)
grep -A1 "name: Game__AbandonedInstanceLifetimeMinutes" <<<"$aj" | grep -q '"30"'               || { echo "a float abandoned instance lifetime must render as digits"; exit 1; }
for bad in "server.game.abandonedInstanceLifetimeMinutes=-1" "server.game.abandonedInstanceLifetimeMinutes=1.5" "server.game.abandonedInstanceLifetimeMinutes=15m"; do
  if helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set "$bad" >/dev/null 2>&1; then
    echo "rendering with $bad must fail"; exit 1
  fi
done

# Tick thread placement (#1002): neither setting renders unset; a CPU (0 too, and a values-file float) renders as digits,
# a GC CPU list as given; anything else fails.
! grep -q "Game__TickThreadCpu" <<<"$off"                                                      || { echo "tick thread CPU rendered when unset"; exit 1; }
! grep -q "DOTNET_GCHeapAffinitizeRanges" <<<"$off"                                            || { echo "GC heap ranges rendered when unset"; exit 1; }
tt=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set server.tickThread.cpu=8 --set-string 'server.tickThread.gcHeapAffinitizeRanges=0-7\,9-19\,21-23')
grep -A1 "name: Game__TickThreadCpu" <<<"$tt" | grep -q '"8"'                                   || { echo "tick thread CPU must render"; exit 1; }
grep -A1 "name: DOTNET_GCHeapAffinitizeRanges" <<<"$tt" | grep -q '"0-7,9-19,21-23"'           || { echo "GC heap ranges must render"; exit 1; }
for n in 0 11; do
  tj=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set-json "server.tickThread.cpu=$n")
  grep -A1 "name: Game__TickThreadCpu" <<<"$tj" | grep -q "\"$n\""                              || { echo "tick thread CPU $n must render as digits"; exit 1; }
done
# A GC CPU list that includes the tick's CPU fails, as a range or on its own.
for ranges in '0-23' '0-7\,8\,9-23' '8'; do
  if helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set server.tickThread.cpu=8 --set-string "server.tickThread.gcHeapAffinitizeRanges=$ranges" >/dev/null 2>&1; then
    echo "GC heap ranges $ranges holding the tick's CPU must fail"; exit 1
  fi
done
for bad in "server.tickThread.cpu=-1""server.tickThread.cpu=1.5" "server.tickThread.cpu=eight" "server.tickThread.gcHeapAffinitizeRanges=0-7;9" "server.tickThread.gcHeapAffinitizeRanges=all"; do
  if helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set "$bad" >/dev/null 2>&1; then
    echo "rendering with $bad must fail"; exit 1
  fi
done

# The per-source admission budget (#1052): no setting renders unset; each limit (a values-file float too) renders as
# digits, each exempt source as given; a limit that is not a whole number of at least 1 fails.
! grep -q "World__Admission__M" <<<"$off"                                                      || { echo "admission limits rendered when unset"; exit 1; }
! grep -q "World__Admission__ExemptSources" <<<"$off"                                          || { echo "exempt sources rendered when unset"; exit 1; }
ad=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set-json server.admission.maxInFlightPerSource=32 --set-json server.admission.maxInFlightPerIPv6Block=64 --set server.admission.maxPerSourcePerMinute=240 --set server.admission.maxPerIPv6BlockPerMinute=960 --set 'server.admission.exemptSources[0]=10.10.1.21/32')
grep -A1 "name: World__Admission__MaxInFlightPerSource" <<<"$ad" | grep -q '"32"'               || { echo "admission cap must render as digits"; exit 1; }
grep -A1 "name: World__Admission__MaxInFlightPerIPv6Block" <<<"$ad" | grep -q '"64"'            || { echo "IPv6 block cap must render as digits"; exit 1; }
grep -A1 "name: World__Admission__MaxPerSourcePerMinute" <<<"$ad" | grep -q '"240"'             || { echo "admission rate must render"; exit 1; }
grep -A1 "name: World__Admission__MaxPerIPv6BlockPerMinute" <<<"$ad" | grep -q '"960"'          || { echo "IPv6 block rate must render"; exit 1; }
grep -A1 "name: World__Admission__ExemptSources__0" <<<"$ad" | grep -q '"10.10.1.21/32"'        || { echo "exempt source must render"; exit 1; }
for key in maxInFlightPerSource maxInFlightPerIPv6Block maxPerSourcePerMinute maxPerIPv6BlockPerMinute; do
  for bad in 0 -1 1.5 many; do
    if helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set "server.admission.$key=$bad" >/dev/null 2>&1; then
      echo "rendering with server.admission.$key=$bad must fail"; exit 1
    fi
  done
done

# PROXY v2 trusts whoever reaches the port (#1052): on a LoadBalancer it needs loadBalancerSourceRanges, on any other
# type networkPolicy; each refusal names the settings, and a ClusterIP with the policy renders.
PROXIED=(--set existingSecret=x --set server.proxyProtocol.enabled=true --set 'server.proxyProtocol.trustedProxies[0]=10.42.0.0/16')
if message=$(helm template t . "${AUTHENTICATION[@]}" $CACHE "${PROXIED[@]}" --set service.type=LoadBalancer 2>&1); then
  echo "proxy protocol on a LoadBalancer without loadBalancerSourceRanges must fail"; exit 1
fi
grep -Fq "server.proxyProtocol.enabled requires service.loadBalancerSourceRanges" <<<"$message" || { echo "the LoadBalancer refusal must name the settings"; exit 1; }
if message=$(helm template t . "${AUTHENTICATION[@]}" $CACHE "${PROXIED[@]}" --set 'service.loadBalancerSourceRanges[0]=10.10.1.17/32' 2>&1); then
  echo "proxy protocol on a ClusterIP without networkPolicy must fail"; exit 1
fi
grep -Fq "server.proxyProtocol.enabled with service.type ClusterIP requires networkPolicy.enabled" <<<"$message" || { echo "the ClusterIP refusal must name the settings"; exit 1; }
helm template t . "${AUTHENTICATION[@]}" $CACHE "${PROXIED[@]}" --set networkPolicy.enabled=true --set networkPolicy.serviceLB=true >/dev/null \
                                                                                                || { echo "proxy protocol on a ClusterIP with networkPolicy must render"; exit 1; }

# The NetworkPolicy (#1052): off by default; serviceLB admits this Service's k3s ServiceLB pods on the game port, and
# gamePort adds peers; enabled with neither fails.
! grep -q "kind: NetworkPolicy" <<<"$off"                                                      || { echo "the NetworkPolicy must be off by default"; exit 1; }
np=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set networkPolicy.enabled=true --set networkPolicy.serviceLB=true --set-json 'networkPolicy.gamePort=[{"podSelector":{"matchLabels":{"app":"edge"}}}]' --show-only templates/networkpolicy.yaml)
grep -q 'svccontroller.k3s.cattle.io/svcname: "t-avalon-world"' <<<"$np"                       || { echo "serviceLB must select this Service's svclb pods"; exit 1; }
grep -q 'kubernetes.io/metadata.name: kube-system' <<<"$np"                                     || { echo "serviceLB must look in kube-system"; exit 1; }
grep -q 'app: edge' <<<"$np"                                                                    || { echo "gamePort peers must render"; exit 1; }
grep -q 'port: 21001' <<<"$np"                                                                  || { echo "the policy must admit the game port"; exit 1; }
if helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set networkPolicy.enabled=true >/dev/null 2>&1; then
  echo "a NetworkPolicy with no peer must fail"; exit 1
fi

# Packet encryption (#875): false unless set; true renders; anything but a boolean fails.
grep -A1 "name: Network__PacketEncryption" <<<"$off" | grep -q '"false"'                       || { echo "packet encryption must default to false"; exit 1; }
pe=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set server.network.packetEncryption=true)
grep -A1 "name: Network__PacketEncryption" <<<"$pe" | grep -q '"true"'                          || { echo "packet encryption true must render"; exit 1; }
if helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set-string server.network.packetEncryption=yes >/dev/null 2>&1; then
  echo "a non-boolean packet encryption must fail"; exit 1
fi

# Each required transport setting refuses omission for its own reason.
for setting in server.transport.existingSecret server.admission.apiUrl server.admission.serverId server.admission.apiCertificateSecret server.admission.apiCertificateKey; do
  if message=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set-string "$setting=" 2>&1); then
    echo "missing $setting must fail"; exit 1
  fi
  grep -Fq "$setting is required" <<<"$message" || { echo "missing $setting must name the setting"; exit 1; }
done
grep -A5 'name: World__Admission__ApiCertificateSha256' <<<"$out" | grep -q 'name: "test-workload-bindings"' || { echo "API certificate pin must use its Secret reference"; exit 1; }
grep -A5 'name: World__Admission__ApiCertificateSha256' <<<"$out" | grep -q 'key: "api-tls-sha256"' || { echo "API certificate pin must use its Secret key"; exit 1; }
grep -A4 'name: Hosting__Security__CertificatePassword' <<<"$out" | grep -q 'name: "test-world-transport"' || { echo "world TLS must use its transport Secret"; exit 1; }

# Log levels (#834): unset renders nothing, a level and per-category overrides render Serilog settings, bad ones fail.
! grep -q "Serilog__" <<<"$off"                                                                     || { echo "log level env rendered without a value"; exit 1; }
lg=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set logging.minimumLevel=Debug --set 'logging.overrides.Avalon\.World\.Entities\.CharacterEntity=Warning')
grep -A1 "name: Serilog__MinimumLevel__Default" <<<"$lg" | grep -q '"Debug"'                        || { echo "minimum level must render"; exit 1; }
grep -A1 "name: Serilog__MinimumLevel__Override__Avalon.World.Entities.CharacterEntity" <<<"$lg" | grep -q '"Warning"' || { echo "category override must render"; exit 1; }
for bad in "logging.minimumLevel=Loud" "logging.overrides.Avalon=Loud"; do
  if helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set "$bad" >/dev/null 2>&1; then
    echo "rendering with $bad must fail"; exit 1
  fi
done

echo "avalon-world chart OK"
