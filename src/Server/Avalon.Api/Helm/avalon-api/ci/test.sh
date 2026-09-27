#!/usr/bin/env bash
# Renders the chart the way homelab and the release use it and asserts on the output.
set -euo pipefail
cd "$(dirname "$0")/.."
KEY=$(printf 'k%.0s' $(seq 1 64))
CACHE="--set cache.host=redis:6379"
# One world whose strings live in a Secret the operator manages, under keys the values name (#523).
W1=(--set worlds.1.worldKey=world-one --set worlds.1.charactersKey=characters-one)

helm lint . $CACHE --set authentication.issuerSigningKey="$KEY" \
  --set worlds.1.world.connectionString=w1 --set worlds.1.characters.connectionString=c1

out=$(helm template t . $CACHE --set existingSecret=avalon-api "${W1[@]}")
grep -q "kind: Deployment" <<<"$out"                                   || { echo "not a Deployment"; exit 1; }
! grep -q "kind: StatefulSet" <<<"$out"                                || { echo "still a StatefulSet"; exit 1; }
! grep -q "kind: HorizontalPodAutoscaler" <<<"$out"                    || { echo "HPA on by default"; exit 1; }
grep -q 'image: "ghcr.io/woozchucky/avalon-server/api:0.0.0-dev"' <<<"$out" || { echo "tag must default to appVersion"; exit 1; }
grep -q "path: /alive" <<<"$out" && grep -q "path: /health" <<<"$out"  || { echo "probes missing"; exit 1; }
grep -A1 "Application__MapAssets__ChunkAssetRoot" <<<"$out" | grep -q '/app/Maps' || { echo "map root missing"; exit 1; }
grep -A1 "ASPNETCORE_ENVIRONMENT" <<<"$out" | grep -q 'Production'    || { echo "environment missing"; exit 1; }
! grep -q "kind: Secret" <<<"$out"                                     || { echo "rendered a Secret despite existingSecret"; exit 1; }

# Worlds (#523): one world, its keys as named, and no single World/Characters pair any more.
grep -A4 "name: Database__Worlds__1__World__ConnectionString" <<<"$out" | grep -q 'key: "world-one"'           || { echo "world 1's world string must come from its key"; exit 1; }
grep -A4 "name: Database__Worlds__1__Characters__ConnectionString" <<<"$out" | grep -q 'key: "characters-one"' || { echo "world 1's characters string must come from its key"; exit 1; }
! grep -Eq "name: Database__(World|Characters)__ConnectionString" <<<"$out"                                  || { echo "the single world pair must be gone"; exit 1; }

# Several worlds, chart-managed Secret, default keys.
many=$(helm template t . $CACHE --set authentication.issuerSigningKey="$KEY" \
  --set worlds.1.world.connectionString=w1 --set worlds.1.characters.connectionString=c1 \
  --set worlds.3.world.connectionString=w3 --set worlds.3.characters.connectionString=c3)
for id in 1 3; do
  grep -A4 "name: Database__Worlds__${id}__World__ConnectionString" <<<"$many" | grep -q "key: \"database-world-${id}-connection-string\""           || { echo "world $id's world env missing"; exit 1; }
  grep -A4 "name: Database__Worlds__${id}__Characters__ConnectionString" <<<"$many" | grep -q "key: \"database-characters-${id}-connection-string\"" || { echo "world $id's characters env missing"; exit 1; }
done
grep -q 'database-world-3-connection-string: "w3"' <<<"$many"          || { echo "world 3's string missing from the Secret"; exit 1; }
grep -q 'database-characters-1-connection-string: "c1"' <<<"$many"     || { echo "world 1's characters string missing from the Secret"; exit 1; }
! grep -q "Database__Worlds__2__" <<<"$many"                           || { echo "rendered a world nobody configured"; exit 1; }

# Every refusal below renders with a cache host, so it fails for its own reason.
must_fail() { local why=$1; shift; if helm template t . $CACHE "$@" >/dev/null 2>&1; then echo "$why"; exit 1; fi; }
must_fail "no world must fail"                        --set existingSecret=x
must_fail "world id 0 must fail"                      --set existingSecret=x --set worlds.0.worldKey=a --set worlds.0.charactersKey=b
must_fail "a non-numeric world id must fail"          --set existingSecret=x --set worlds.abc.worldKey=a --set worlds.abc.charactersKey=b
must_fail "a leading zero must fail"                  --set existingSecret=x --set worlds.01.worldKey=a --set worlds.01.charactersKey=b
must_fail "a world id above 65535 must fail"          --set existingSecret=x --set worlds.70000.worldKey=a --set worlds.70000.charactersKey=b
must_fail "half a pair must fail"                     --set authentication.issuerSigningKey="$KEY" --set worlds.1.world.connectionString=w1
must_fail "existingSecret + inline world string must fail" --set existingSecret=x --set worlds.1.world.connectionString=leak
must_fail "the old single pair must fail"             --set existingSecret=x "${W1[@]}" --set database.world.connectionString=old
msg=$(helm template t . $CACHE --set existingSecret=x 2>&1 || true)
grep -q "worlds lists no world" <<<"$msg"                              || { echo "the no-world refusal must say why"; exit 1; }

# Secret key names (#523): the chart-managed Secret names its own keys, and no two settings may read one key.
CS1=(--set worlds.1.world.connectionString=w1 --set worlds.1.characters.connectionString=c1)
must_fail "worldKey without existingSecret must fail"      --set authentication.issuerSigningKey="$KEY" "${CS1[@]}" --set worlds.1.worldKey=custom
must_fail "charactersKey without existingSecret must fail" --set authentication.issuerSigningKey="$KEY" "${CS1[@]}" --set worlds.1.charactersKey=custom
for own in jwt-signing-key database-auth-connection-string cache-password notification-private-key; do
  must_fail "a world key reusing $own must fail"          --set existingSecret=x --set worlds.1.worldKey="$own" --set worlds.1.charactersKey=characters-one
done
must_fail "worldKey == charactersKey must fail"            --set existingSecret=x --set worlds.1.worldKey=same --set worlds.1.charactersKey=same
must_fail "two worlds sharing a key must fail"             --set existingSecret=x "${W1[@]}" --set worlds.2.worldKey=world-one --set worlds.2.charactersKey=characters-two
must_fail "a key reusing another world's default must fail" --set existingSecret=x --set worlds.1.worldKey= --set worlds.2.worldKey=database-world-1-connection-string --set worlds.2.charactersKey=characters-two
must_fail "a key with a slash must fail"                   --set existingSecret=x --set worlds.1.worldKey=bad/key --set worlds.1.charactersKey=characters-one
must_fail "a key with a space must fail"                   --set existingSecret=x --set 'worlds.1.worldKey=bad key' --set worlds.1.charactersKey=characters-one
msg=$(helm template t . $CACHE --set existingSecret=x --set worlds.1.worldKey=jwt-signing-key --set worlds.1.charactersKey=characters-one 2>&1 || true)
grep -q "authentication.issuerSigningKey" <<<"$msg"                    || { echo "the key collision must name what already reads the key"; exit 1; }
blank=$(helm template t . $CACHE --set existingSecret=x --set 'worlds.1.worldKey=  ' --set worlds.1.charactersKey=' characters-one ')
grep -A4 "name: Database__Worlds__1__World__ConnectionString" <<<"$blank" | grep -q 'key: "database-world-1-connection-string"' || { echo "a blank worldKey must mean the default key"; exit 1; }
grep -A4 "name: Database__Worlds__1__Characters__ConnectionString" <<<"$blank" | grep -q 'key: "characters-one"$'            || { echo "a key must be trimmed"; exit 1; }

num=$(helm template t . $CACHE --set existingSecret=x --set worlds.1.worldKey=123 --set worlds.1.charactersKey=true)
grep -A4 "name: Database__Worlds__1__World__ConnectionString" <<<"$num" | grep -q 'key: "123"'        || { echo "a numeric key must render as a string"; exit 1; }
grep -A4 "name: Database__Worlds__1__Characters__ConnectionString" <<<"$num" | grep -q 'key: "true"' || { echo "a key that reads as a boolean must render as a string"; exit 1; }

# A changed world string restarts the pods: the checksum annotation follows the chart-managed Secret.
sum() { helm template t . $CACHE --set authentication.issuerSigningKey="$KEY" "$@" | grep "checksum/secret:"; }
a=$(sum "${CS1[@]}"); b=$(sum --set worlds.1.world.connectionString=w1-rotated --set worlds.1.characters.connectionString=c1)
[ -n "$a" ] && [ "$a" != "$b" ]                                        || { echo "a changed world string must change the checksum"; exit 1; }

hpa=$(helm template t . $CACHE --set existingSecret=x "${W1[@]}" --set autoscaling.enabled=true)
grep -A2 "scaleTargetRef" <<<"$hpa" | grep -q "kind: Deployment"       || { echo "HPA must target the Deployment"; exit 1; }

must_fail "existingSecret + inline secret must fail" --set existingSecret=x "${W1[@]}" --set cache.password=leak
ot=$(helm template t . $CACHE --set existingSecret=x "${W1[@]}" --set otel.endpoint=http://otel-collector:4317 --set 'otel.resourceAttributes.deployment\.environment=production')
grep -A1 "name: OTEL_EXPORTER_OTLP_ENDPOINT" <<<"$ot" | grep -q "http://otel-collector:4317" || { echo "otel endpoint missing"; exit 1; }
grep -A1 "name: OTEL_SERVICE_NAME" <<<"$ot" | grep -q '"t-avalon-api"'                                  || { echo "service name must default to the fullname"; exit 1; }
grep -A1 "name: OTEL_RESOURCE_ATTRIBUTES" <<<"$ot" | grep -q '"deployment.environment=production"'          || { echo "resource attributes missing"; exit 1; }
! grep -q "OTEL_" <<<"$out"                                                                              || { echo "otel env rendered without an endpoint"; exit 1; }
grep -A1 "name: Application__Cache__Host" <<<"$out" | grep -q '"redis:6379"'              || { echo "cache host missing"; exit 1; }
# With a world configured, so the cache host is the only thing missing (#543).
if helm template t . --set existingSecret=x "${W1[@]}" >/dev/null 2>&1; then
  echo "rendering without cache.host must fail"; exit 1
fi
echo "avalon-api chart OK"
