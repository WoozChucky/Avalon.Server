#!/usr/bin/env bash
# Renders the chart the way homelab and the release use it and asserts on the output.
set -euo pipefail
cd "$(dirname "$0")/.."
# Git Bash on Windows would otherwise rewrite arguments such as pathPrefix=/api into Windows paths.
export MSYS_NO_PATHCONV=1

# Supply mandatory authentication settings for renders and unrelated refusal checks.
AUTHENTICATION=(--values ci/authentication-values.yaml)
KEY=$(printf 'k%.0s' $(seq 1 64))
CACHE="--set cache.host=redis:6379"
# The auth string a chart-managed Secret needs (#564).
AUTH=(--set database.auth.connectionString=a1)
# One world whose strings live in a Secret the operator manages, under keys the values name (#523).
W1=(--set worlds.1.worldKey=world-one --set worlds.1.charactersKey=characters-one)

helm lint . "${AUTHENTICATION[@]}" $CACHE --set authentication.issuerSigningKey="$KEY" "${AUTH[@]}" \
  --set worlds.1.world.connectionString=w1 --set worlds.1.characters.connectionString=c1

out=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=avalon-api "${W1[@]}")
grep -q "kind: Deployment" <<<"$out"                                   || { echo "not a Deployment"; exit 1; }
! grep -q "kind: StatefulSet" <<<"$out"                                || { echo "still a StatefulSet"; exit 1; }
! grep -q "kind: HorizontalPodAutoscaler" <<<"$out"                    || { echo "HPA on by default"; exit 1; }
grep -q 'image: "ghcr.io/woozchucky/avalon-server/api:0.0.0-dev"' <<<"$out" || { echo "tag must default to appVersion"; exit 1; }
grep -q "path: /alive" <<<"$out" && grep -q "path: /health" <<<"$out"  || { echo "probes missing"; exit 1; }
grep -A1 "Application__MapAssets__ChunkAssetRoot" <<<"$out" | grep -q '/app/Maps' || { echo "map root missing"; exit 1; }
grep -A1 "ASPNETCORE_ENVIRONMENT" <<<"$out" | grep -q 'Production'    || { echo "environment missing"; exit 1; }
! grep -q "kind: Secret" <<<"$out"                                     || { echo "rendered a Secret despite existingSecret"; exit 1; }

# The public tooltips' default world: only when set, so the API's fallback applies otherwise.
! grep -q "Application__PublicWorldId" <<<"$out"                       || { echo "publicWorldId must not render unset"; exit 1; }
pw=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=avalon-api "${W1[@]}" --set publicWorldId=2)
grep -A1 "name: Application__PublicWorldId" <<<"$pw" | grep -q 'value: "2"' || { echo "publicWorldId must render as Application__PublicWorldId"; exit 1; }

# The link previews' public site URL: only when set, so previews leave og:url out otherwise.
! grep -q "Application__PublicSiteUrl" <<<"$out"                       || { echo "publicSiteUrl must not render unset"; exit 1; }
ps=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=avalon-api "${W1[@]}" --set publicSiteUrl=https://avalon.example)
grep -A1 "name: Application__PublicSiteUrl" <<<"$ps" | grep -q 'value: "https://avalon.example"' || { echo "publicSiteUrl must render as Application__PublicSiteUrl"; exit 1; }

# Worlds (#523): one world, its keys as named, and no single World/Characters pair any more.
grep -A4 "name: Database__Worlds__1__World__ConnectionString" <<<"$out" | grep -q 'key: "world-one"'           || { echo "world 1's world string must come from its key"; exit 1; }
grep -A4 "name: Database__Worlds__1__Characters__ConnectionString" <<<"$out" | grep -q 'key: "characters-one"' || { echo "world 1's characters string must come from its key"; exit 1; }
! grep -Eq "name: Database__(World|Characters)__ConnectionString" <<<"$out"                                  || { echo "the single world pair must be gone"; exit 1; }

# Several worlds, chart-managed Secret, default keys.
many=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set authentication.issuerSigningKey="$KEY" "${AUTH[@]}" \
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
must_fail() { local why=$1; shift; if helm template t . "${AUTHENTICATION[@]}" $CACHE "$@" >/dev/null 2>&1; then echo "$why"; exit 1; fi; }
must_fail "no world must fail"                        --set existingSecret=x
must_fail "world id 0 must fail"                      --set existingSecret=x --set worlds.0.worldKey=a --set worlds.0.charactersKey=b
must_fail "a non-numeric world id must fail"          --set existingSecret=x --set worlds.abc.worldKey=a --set worlds.abc.charactersKey=b
must_fail "a leading zero must fail"                  --set existingSecret=x --set worlds.01.worldKey=a --set worlds.01.charactersKey=b
must_fail "a world id above 65535 must fail"          --set existingSecret=x --set worlds.70000.worldKey=a --set worlds.70000.charactersKey=b
must_fail "half a pair must fail"                     --set authentication.issuerSigningKey="$KEY" "${AUTH[@]}" --set worlds.1.world.connectionString=w1
must_fail "existingSecret + inline world string must fail" --set existingSecret=x --set worlds.1.world.connectionString=leak
must_fail "the old single pair must fail"             --set existingSecret=x "${W1[@]}" --set database.world.connectionString=old
msg=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x 2>&1 || true)
grep -q "worlds lists no world" <<<"$msg"                              || { echo "the no-world refusal must say why"; exit 1; }

# Secret key names (#523): the chart-managed Secret names its own keys, and no two settings may read one key.
CS1=(--set worlds.1.world.connectionString=w1 --set worlds.1.characters.connectionString=c1)
must_fail "worldKey without existingSecret must fail"      --set authentication.issuerSigningKey="$KEY" "${AUTH[@]}" "${CS1[@]}" --set worlds.1.worldKey=custom
must_fail "charactersKey without existingSecret must fail" --set authentication.issuerSigningKey="$KEY" "${AUTH[@]}" "${CS1[@]}" --set worlds.1.charactersKey=custom
for own in jwt-signing-key database-auth-connection-string cache-password notification-private-key distribution-secret-key; do
  must_fail "a world key reusing $own must fail"          --set existingSecret=x --set worlds.1.worldKey="$own" --set worlds.1.charactersKey=characters-one
done
must_fail "worldKey == charactersKey must fail"            --set existingSecret=x --set worlds.1.worldKey=same --set worlds.1.charactersKey=same
must_fail "two worlds sharing a key must fail"             --set existingSecret=x "${W1[@]}" --set worlds.2.worldKey=world-one --set worlds.2.charactersKey=characters-two
must_fail "a key reusing another world's default must fail" --set existingSecret=x --set worlds.1.worldKey= --set worlds.2.worldKey=database-world-1-connection-string --set worlds.2.charactersKey=characters-two
must_fail "a key with a slash must fail"                   --set existingSecret=x --set worlds.1.worldKey=bad/key --set worlds.1.charactersKey=characters-one
must_fail "a key with a space must fail"                   --set existingSecret=x --set 'worlds.1.worldKey=bad key' --set worlds.1.charactersKey=characters-one
msg=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set worlds.1.worldKey=jwt-signing-key --set worlds.1.charactersKey=characters-one 2>&1 || true)
grep -q "authentication.issuerSigningKey" <<<"$msg"                    || { echo "the key collision must name what already reads the key"; exit 1; }
blank=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set 'worlds.1.worldKey=  ' --set worlds.1.charactersKey=' characters-one ')
grep -A4 "name: Database__Worlds__1__World__ConnectionString" <<<"$blank" | grep -q 'key: "database-world-1-connection-string"' || { echo "a blank worldKey must mean the default key"; exit 1; }
grep -A4 "name: Database__Worlds__1__Characters__ConnectionString" <<<"$blank" | grep -q 'key: "characters-one"$'            || { echo "a key must be trimmed"; exit 1; }

num=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x --set worlds.1.worldKey=123 --set worlds.1.charactersKey=true)
grep -A4 "name: Database__Worlds__1__World__ConnectionString" <<<"$num" | grep -q 'key: "123"'        || { echo "a numeric key must render as a string"; exit 1; }
grep -A4 "name: Database__Worlds__1__Characters__ConnectionString" <<<"$num" | grep -q 'key: "true"' || { echo "a key that reads as a boolean must render as a string"; exit 1; }

# The auth string (#564): required and trimmed with the chart-managed Secret, like the others;
# with existingSecret only its key is referenced, so no inline string is needed ($out above).
must_fail "a chart-managed Secret without the auth string must fail" --set authentication.issuerSigningKey="$KEY" "${CS1[@]}"
must_fail "a whitespace-only auth string must fail"                  --set authentication.issuerSigningKey="$KEY" "${CS1[@]}" --set 'database.auth.connectionString=  '
msg=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set authentication.issuerSigningKey="$KEY" "${CS1[@]}" 2>&1 || true)
grep -q "database.auth.connectionString is required" <<<"$msg"         || { echo "the missing auth string refusal must name the value"; exit 1; }
auth=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set authentication.issuerSigningKey="$KEY" "${CS1[@]}" --set 'database.auth.connectionString= a1 ')
grep -q 'database-auth-connection-string: "a1"$' <<<"$auth"            || { echo "the auth string must reach the Secret, trimmed"; exit 1; }
grep -A4 "name: Database__Auth__ConnectionString" <<<"$out" | grep -q "key: database-auth-connection-string" || { echo "existingSecret must reference the auth key"; exit 1; }

# A changed world string restarts the pods: the checksum annotation follows the chart-managed Secret.
sum() { helm template t . "${AUTHENTICATION[@]}" $CACHE --set authentication.issuerSigningKey="$KEY" "${AUTH[@]}" "$@" | grep "checksum/secret:"; }
a=$(sum "${CS1[@]}"); b=$(sum --set worlds.1.world.connectionString=w1-rotated --set worlds.1.characters.connectionString=c1)
[ -n "$a" ] && [ "$a" != "$b" ]                                        || { echo "a changed world string must change the checksum"; exit 1; }

hpa=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x "${W1[@]}" --set autoscaling.enabled=true)
grep -A2 "scaleTargetRef" <<<"$hpa" | grep -q "kind: Deployment"       || { echo "HPA must target the Deployment"; exit 1; }

must_fail "existingSecret + inline secret must fail" --set existingSecret=x "${W1[@]}" --set cache.password=leak
ot=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x "${W1[@]}" --set otel.endpoint=http://otel-collector:4317 --set 'otel.resourceAttributes.deployment\.environment=production')
grep -A1 "name: OTEL_EXPORTER_OTLP_ENDPOINT" <<<"$ot" | grep -q "http://otel-collector:4317" || { echo "otel endpoint missing"; exit 1; }
grep -A1 "name: OTEL_SERVICE_NAME" <<<"$ot" | grep -q '"t-avalon-api"'                                  || { echo "service name must default to the fullname"; exit 1; }
grep -A1 "name: OTEL_RESOURCE_ATTRIBUTES" <<<"$ot" | grep -q '"deployment.environment=production"'          || { echo "resource attributes missing"; exit 1; }
! grep -q "OTEL_" <<<"$out"                                                                              || { echo "otel env rendered without an endpoint"; exit 1; }
grep -A1 "name: Application__Cache__Host" <<<"$out" | grep -q '"redis:6379"'              || { echo "cache host missing"; exit 1; }
# With a world configured, so the cache host is the only thing missing (#543).
if helm template t . "${AUTHENTICATION[@]}" --set existingSecret=x "${W1[@]}" >/dev/null 2>&1; then
  echo "rendering without cache.host must fail"; exit 1
fi
! grep -q "Application__Distribution__" <<<"$out"                                              || { echo "distribution env rendered without an endpoint"; exit 1; }
dist=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x "${W1[@]}" --set distribution.endpoint=http://garage.distribution.svc:3900 --set distribution.publicUrl=https://dist.example --set distribution.accessKeyId=GKabc)
grep -A1 "name: Application__Distribution__Endpoint" <<<"$dist" | grep -q "http://garage.distribution.svc:3900" || { echo "distribution endpoint missing"; exit 1; }
grep -A1 "name: Application__Distribution__PublicUrl" <<<"$dist" | grep -q "https://dist.example"                || { echo "distribution public url missing"; exit 1; }
grep -A1 "name: Application__Distribution__Bucket" <<<"$dist" | grep -q '"avalon-dist"'                            || { echo "distribution bucket missing"; exit 1; }
grep -A1 "name: Application__Distribution__Region" <<<"$dist" | grep -q '"garage"'                                 || { echo "distribution region missing"; exit 1; }
grep -A1 "name: Application__Distribution__AccessKeyId" <<<"$dist" | grep -q '"GKabc"'                             || { echo "distribution key id missing"; exit 1; }
grep -A4 "name: Application__Distribution__SecretAccessKey" <<<"$dist" | grep -q "key: distribution-secret-key"    || { echo "distribution secret must come from the Secret"; exit 1; }
# With a world configured, so the inline secret is the only thing refused.
if helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x "${W1[@]}" --set distribution.secretAccessKey=leak >/dev/null 2>&1; then
  echo "existingSecret + inline distribution.secretAccessKey must fail"; exit 1
fi
# Rate limiting (#561): nothing rendered by default, so the API's defaults apply; each value when set,
# false and 0 included (the API refuses a limit below 1 itself, naming the setting).
! grep -q "Application__RateLimiting__" <<<"$out"                                          || { echo "rate limiting env rendered without a value"; exit 1; }
rl=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x "${W1[@]}" --set rateLimiting.enabled=false \
  --set rateLimiting.anonymousPermitsPerMinute=0 --set rateLimiting.authenticatedPermitsPerMinute=600 \
  --set rateLimiting.clientAuthPermitsPerMinute=7)
grep -A1 "name: Application__RateLimiting__Enabled" <<<"$rl" | grep -q '"false"'                       || { echo "rateLimiting.enabled=false missing"; exit 1; }
grep -A1 "name: Application__RateLimiting__AnonymousPermitsPerMinute" <<<"$rl" | grep -q '"0"'         || { echo "rateLimiting.anonymousPermitsPerMinute missing"; exit 1; }
grep -A1 "name: Application__RateLimiting__AuthenticatedPermitsPerMinute" <<<"$rl" | grep -q '"600"'   || { echo "rateLimiting.authenticatedPermitsPerMinute missing"; exit 1; }
grep -A1 "name: Application__RateLimiting__ClientAuthPermitsPerMinute" <<<"$rl" | grep -q '"7"'        || { echo "rateLimiting.clientAuthPermitsPerMinute missing"; exit 1; }
on=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x "${W1[@]}" --set rateLimiting.enabled=true)
grep -A1 "name: Application__RateLimiting__Enabled" <<<"$on" | grep -q '"true"'                        || { echo "rateLimiting.enabled=true missing"; exit 1; }
! grep -q "Application__RateLimiting__AnonymousPermitsPerMinute" <<<"$on"                             || { echo "an unset limit must not render"; exit 1; }
# Balance service (admin /balance proxy): the URL renders only when set; the secret is always a
# secretKeyRef, optional, so an unset key leaves the admin endpoints answering 503.
! grep -q "Application__Balance__Url" <<<"$out"                                                || { echo "balance url rendered without a value"; exit 1; }
grep -A4 "name: Application__Balance__SharedSecret" <<<"$out" | grep -q "key: balance-shared-secret" || { echo "balance secret must come from the Secret"; exit 1; }
grep -A5 "name: Application__Balance__SharedSecret" <<<"$out" | grep -q "optional: true"       || { echo "balance secret must be optional"; exit 1; }
bal=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x "${W1[@]}" --set balance.url=http://balance-avalon-balance:8080)
grep -A1 "name: Application__Balance__Url" <<<"$bal" | grep -q '"http://balance-avalon-balance:8080"' || { echo "balance url missing"; exit 1; }
must_fail "existingSecret + inline balance.sharedSecret must fail" --set existingSecret=x "${W1[@]}" --set balance.sharedSecret=leak
balsec=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set authentication.issuerSigningKey="$KEY" "${AUTH[@]}" "${CS1[@]}" --set balance.sharedSecret=s3cret)
grep -q 'balance-shared-secret: "s3cret"' <<<"$balsec"                                         || { echo "balance secret must reach the chart-managed Secret"; exit 1; }
# Live template editing: the editable worlds (indexed env) and the reload timeout render only when set.
! grep -q "Application__Templates__" <<<"$out"                                                 || { echo "templates env rendered without a value"; exit 1; }
tpl=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x "${W1[@]}" --set 'templates.editableWorlds={2,5}' --set templates.reloadTimeout=00:00:20)
grep -A1 "name: Application__Templates__EditableWorlds__0" <<<"$tpl" | grep -q '"2"'           || { echo "editableWorlds[0] missing"; exit 1; }
grep -A1 "name: Application__Templates__EditableWorlds__1" <<<"$tpl" | grep -q '"5"'           || { echo "editableWorlds[1] missing"; exit 1; }
! grep -q "Application__Templates__EditableWorlds__2" <<<"$tpl"                                || { echo "rendered an extra editable world"; exit 1; }
grep -A1 "name: Application__Templates__ReloadTimeout" <<<"$tpl" | grep -q '"00:00:20"'        || { echo "reloadTimeout missing"; exit 1; }
tpo=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x "${W1[@]}" --set templates.reloadTimeout=00:00:20)
! grep -q "Application__Templates__EditableWorlds" <<<"$tpo"                                   || { echo "empty editableWorlds must not render"; exit 1; }

# Authentication values cannot be omitted, even when all other configuration is valid.
must_refuse_authentication() {
  local setting=$1 expected=$2 message
  if message=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x "${W1[@]}" --set-string "$setting=" 2>&1); then
    echo "missing $setting must fail"; exit 1
  fi
  grep -Fq "$expected" <<<"$message" || { echo "missing $setting must name the setting"; exit 1; }
}
for setting in storeAuthentication.steamAppId storeAuthentication.existingSecret storeAuthentication.publisherKeyKey gameAdmission.tlsExistingSecret gameAdmission.bindingsExistingSecret; do
  must_refuse_authentication "$setting" "$setting is required"
done
for field in serverId worldId tlsServerName; do
  must_refuse_authentication "gameAdmission.servers[0].$field" "gameAdmission $field is required"
done
if message=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x "${W1[@]}" --set-json 'gameAdmission.servers=[]' 2>&1); then
  echo "empty workload bindings must fail"; exit 1
fi
grep -Fq 'gameAdmission.servers is required' <<<"$message" || { echo "empty workload bindings must name the setting"; exit 1; }
for value in 0 -1 1.5 4294967296 invalid; do
  if message=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x "${W1[@]}" --set-string "storeAuthentication.steamAppId=$value" 2>&1); then
    echo "invalid Steam application ID $value must fail"; exit 1
  fi
  grep -Fq 'storeAuthentication.steamAppId' <<<"$message" || { echo "invalid application ID must name the setting"; exit 1; }
done
for value in 123456 2499461 4294967295; do
  configured=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x "${W1[@]}" --set "storeAuthentication.steamAppId=$value")
  grep -A1 'name: Application__StoreAuthentication__SteamAppId' <<<"$configured" | grep -Fq "value: \"$value\"" || { echo "application ID must render as canonical decimal"; exit 1; }
done
grep -A4 'name: Application__StoreAuthentication__SteamPublisherKey' <<<"$out" | grep -q 'name: "test-steam-credentials"' || { echo "publisher key must use its Secret reference"; exit 1; }
grep -A4 'name: Application__GameWorkloads__Servers__0__ClientCertificateSha256' <<<"$out" | grep -q 'key: "world-test-client-sha256"' || { echo "client certificate binding must use its Secret key"; exit 1; }
grep -q 'containerPort: 9443' <<<"$out" || { echo "internal HTTPS port missing"; exit 1; }

# Separate Playtest authority, disabled by default, with indexed allowed world configuration.
profile=(--set storeAuthentication.steamPlaytest.appId=2514590 --set-json 'storeAuthentication.steamPlaytest.allowedWorldIds=[3]')
disabled=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x "${W1[@]}" "${profile[@]}")
grep -A1 'name: Application__StoreAuthentication__SteamPlaytest__Enabled' <<<"$disabled" | grep -q 'value: "false"' || { echo "Playtest must be disabled"; exit 1; }
grep -A1 'name: Application__StoreAuthentication__SteamPlaytest__AppId' <<<"$disabled" | grep -q 'value: "2514590"' || { echo "Playtest AppID missing"; exit 1; }
enabled=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x "${W1[@]}" "${profile[@]}" --set storeAuthentication.steamPlaytest.enabled=true)
grep -A1 'name: Application__StoreAuthentication__SteamPlaytest__Enabled' <<<"$enabled" | grep -q 'value: "true"' || { echo "Playtest enabled missing"; exit 1; }
grep -A1 'name: Application__StoreAuthentication__SteamPlaytest__AllowedWorldIds__0' <<<"$enabled" | grep -q 'value: "3"' || { echo "PTR restriction missing"; exit 1; }
! grep -q 'SteamPlaytest__AllowedWorldIds__1' <<<"$enabled" || { echo "unexpected extra world"; exit 1; }
omitted=$(helm template t . "${AUTHENTICATION[@]}" $CACHE --set existingSecret=x "${W1[@]}" --set-json 'storeAuthentication.steamPlaytest=null')
grep -A1 'name: Application__StoreAuthentication__SteamPlaytest__Enabled' <<<"$omitted" | grep -q 'value: "false"' || { echo "omitted profile must remain main-only"; exit 1; }
for value in 0 -1 1.5 4294967296 invalid 123456; do
  must_fail "invalid Playtest AppID $value must fail" --set existingSecret=x "${W1[@]}" "${profile[@]}" --set storeAuthentication.steamPlaytest.enabled=true --set-string "storeAuthentication.steamPlaytest.appId=$value"
done
for worlds in '[]' '[0]' '[3,3]' '[65536]' '[1.5]' '[true]'; do
  must_fail "invalid Playtest worlds $worlds must fail" --set existingSecret=x "${W1[@]}" "${profile[@]}" --set storeAuthentication.steamPlaytest.enabled=true --set-json "storeAuthentication.steamPlaytest.allowedWorldIds=$worlds"
done
must_fail "invalid disabled profile must fail" --set existingSecret=x "${W1[@]}" --set storeAuthentication.steamPlaytest.appId=-1

# ---------------------------------------------------------------------------------------------------
# Service modes and routes (#794, design D9.2, D7.2). One case per mode.

# The substance of a render, one sorted line per fact: each setting the container reads, with its
# value or its Secret reference, its ports, mounts, volumes and probes, and the Service's ports.
shape() {
  awk '
    /^kind: / { kind = $2; print "kind " kind; section = "" }
    kind == "Deployment" && /^          [a-zA-Z]+:/ { section = $1; sub(/:$/, "", section) }
    kind == "Deployment" && section == "env" && /^            - name: / { name = $3 }
    kind == "Deployment" && section == "env" && /^              value: / { v = $0; sub(/^ *value: /, "", v); print "env " name "=" v }
    kind == "Deployment" && section == "env" && /^                  name: / { secret = $2 }
    kind == "Deployment" && section == "env" && /^                  key: / { print "env " name " <- " secret "/" $2 }
    kind == "Deployment" && section == "env" && /^                  optional: true$/ { print "env " name " optional" }
    kind == "Deployment" && section == "ports" && /^            - name: / { port = $3 }
    kind == "Deployment" && section == "ports" && /containerPort: / { print "port " port " " $2 }
    kind == "Deployment" && section == "volumeMounts" && /mountPath: / { print "mount " $2 }
    kind == "Deployment" && section ~ /Probe$/ && /path: / { probe = section " " $2 }
    kind == "Deployment" && section ~ /Probe$/ && /failureThreshold: / { print "probe " probe " " $2 }
    kind == "Deployment" && /^            secretName: / { print "volume " $2 }
    kind == "Service" && /^    - port: / { servicePort = $3 }
    kind == "Service" && /^      name: / { print "service-port " $2 " " servicePort }
  ' | LC_ALL=C sort
}
HOMELAB=(--values ci/homelab-values.yaml)
# A Windows checkout holds the chart with CRLF line ends; compare what it says, not how lines end.
render() { helm template t . "$@" | tr -d '\r'; }
EXPECTED=$(tr -d '\r' < ci/homelab-render.txt)

# The release homelab runs today names no services: it must render exactly what it rendered before the
# split (ci/homelab-render.txt, taken from the chart before #794), one process running all four
# services, with no Application__Services and no startup probe. Naming all four changes nothing else.
mono=$(render "${HOMELAB[@]}")
diff <(shape <<<"$mono") <(echo "$EXPECTED")                      || { echo "the release without services must render what it rendered before the split"; exit 1; }
four=$(render "${HOMELAB[@]}" --set-json 'services=["identity","worlds","commerce","distribution"]')
diff <(shape <<<"$mono") <(shape <<<"$four" | grep -v '^env Application__Services__') \
                                                                      || { echo "naming all four services must render the same process"; exit 1; }
[ "$(shape <<<"$four" | grep -c '^env Application__Services__')" = 4 ] || { echo "the four services must render as Application__Services__<n>"; exit 1; }

# Each service alone reads exactly the settings and Secret keys design D9.4 gives it, out of what the
# one process reads; only identity has the game admission port, its certificate and no schema wait.
owners() {
  case "$1" in
    Application__StoreAuthentication__Environment|Application__StoreAuthentication__SteamIdentityPrefix) echo "identity commerce" ;;
    Application__StoreAuthentication__*|Application__Email__*|Application__Notification__*|Application__SteamWebLink__*|Application__GameWorkloads__*|Kestrel__Endpoints__GameInternal__*|Application__RateLimiting__ClientAuthPermitsPerMinute) echo identity ;;
    Application__Authentication__SigningKey|Application__Authentication__SigningKeyId|Application__GameAuth__*) echo identity ;;
    Database__Worlds__*__Characters__*) echo "identity worlds" ;;
    Database__Worlds__*__World__*|Application__Templates__*|Application__MapAssets__*|Application__Balance__*|Application__PublicWorldId|Application__PublicSiteUrl) echo worlds ;;
    Application__Commerce__*) echo commerce ;;
    Application__Distribution__*) echo distribution ;;
    Application__Cache__*) echo "identity worlds commerce" ;;
    Database__Auth__*|Application__Authentication__*|Application__ForwardedHeaders__*|Application__RateLimiting__*|Kestrel__Endpoints__Public__*|ASPNETCORE_ENVIRONMENT|DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE|OTEL_*) echo "identity worlds commerce distribution" ;;
  esac
}
# What $1 alone reads of the one process's render $2.
expected_for() {
  local service=$1 line rest
  while IFS= read -r line; do
    case "$line" in
      "env "*) rest=${line#env }; [[ " $(owners "${rest%%[= ]*}") " == *" $service "* ]] && echo "$line" ;;
      "port game-internal "*|"service-port game-internal "*|"mount "*|"volume "*) [ "$service" = identity ] && echo "$line" ;;
      *) echo "$line" ;;
    esac
  done <<<"$2"
  echo "env Application__Services__0=\"$service\""
  if [ "$service" != identity ]; then
    echo 'env Application__Startup__AuthSchemaWaitSeconds="300"'
    echo "probe startupProbe /alive 72"
  fi
}
# Each service alone, rendered with the values after $1, reads exactly its part of the one process's render $1.
each_service_alone() {
  local one=$1 union="" service alone
  shift
  for service in identity worlds commerce distribution; do
    alone=$(render "$@" --set-json "services=[\"$service\"]")
    diff <(shape <<<"$alone") <(expected_for "$service" "$one" | LC_ALL=C sort) || { echo "$service alone must read exactly its own settings (design D9.4)"; exit 1; }
    union+=$(shape <<<"$alone" | grep -v -e '^env Application__Services__' -e '^env Application__Startup__' -e '^probe startupProbe ')$'\n'
    helm lint . --quiet "$@" --set-json "services=[\"$service\"]" >/dev/null || { echo "helm lint failed for $service"; exit 1; }
  done
  diff <(LC_ALL=C sort -u <<<"$union" | sed '/^$/d') <(echo "$one") || { echo "every setting of the one process must reach the service that reads it"; exit 1; }
}
each_service_alone "$EXPECTED" "${HOMELAB[@]}"

# ES256 (#801). The release that moves access tokens to ES256 renders what the homelab release rendered,
# plus identity's private key, key id and game-auth host key (optional: the API falls back to the HS256
# key for that release) and every service's public keys; the HS256 key stays while legacyIssuerSigningKey
# is on, and goes when it is off. Each service alone reads its part: the private key and the host key
# only where identity runs.
ES256=(--values ci/es256-values.yaml)
es256=$(render "${HOMELAB[@]}" "${ES256[@]}")
added=(
  'env Application__Authentication__SigningKey <- avalon-api/jwt-signing-private-key'
  'env Application__Authentication__SigningKeyId="2026-10"'
  'env Application__Authentication__ValidationKeys__2026-10="MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEcNiZzBqacm/Ab3jkvqpj2CrbM6swvAqefzjRdR42P7jfnItCIn+d8Ib+b6aRJPAXeVhCD3wgNrQjgj7P8knbrg=="'
  'env Application__GameAuth__HostKey <- avalon-api/game-auth-host-key'
  'env Application__GameAuth__HostKey optional'
)
diff <(shape <<<"$es256") <(printf '%s\n' "$EXPECTED" "${added[@]}" | LC_ALL=C sort) \
                                                                      || { echo "the ES256 release must add identity's key and the public keys to the homelab render"; exit 1; }
each_service_alone "$(shape <<<"$es256")" "${HOMELAB[@]}" "${ES256[@]}"
diff <(shape <<<"$es256" | grep -v '^env Application__Authentication__IssuerSigningKey ') \
     <(render "${HOMELAB[@]}" "${ES256[@]}" --set authentication.legacyIssuerSigningKey=false | shape) \
                                                                      || { echo "legacyIssuerSigningKey off must leave out the HS256 key alone"; exit 1; }
# A chart-managed Secret holds only the keys of the release's services (design D9.3).
SECRETS=(--set authentication.issuerSigningKey="$KEY" "${AUTH[@]}" "${CS1[@]}" --set cache.password=p --set notification.privateKey=n
         --set distribution.secretAccessKey=d --set balance.sharedSecret=b)
for expect in "identity:cache-password database-auth-connection-string database-characters-1-connection-string jwt-signing-key notification-private-key" \
              "worlds:balance-shared-secret cache-password database-auth-connection-string database-characters-1-connection-string database-world-1-connection-string jwt-signing-key" \
              "commerce:cache-password database-auth-connection-string jwt-signing-key" \
              "distribution:database-auth-connection-string distribution-secret-key jwt-signing-key"; do
  service=${expect%%:*}
  keys=$(render "${AUTHENTICATION[@]}" $CACHE "${SECRETS[@]}" --set-json "services=[\"$service\"]" --show-only templates/secret.yaml \
    | awk '/^stringData:/{f=1; next} f && /^  [a-z]/{sub(/:.*/, ""); print $1}' | LC_ALL=C sort | tr '\n' ' ')
  [ "$keys" = "${expect#*:} " ] || { echo "$service's Secret must hold only its keys, not: $keys"; exit 1; }
done
# With the ES256 values (#801) identity's Secret also holds its private key and the game-auth host key, which no
# other service's holds; the HS256 key stays only with legacyIssuerSigningKey.
ESECRETS=("${SECRETS[@]}" "${ES256[@]}" --set authentication.signingKey=private --set gameAuth.hostKey=host)
for expect in "identity:true:cache-password database-auth-connection-string database-characters-1-connection-string game-auth-host-key jwt-signing-key jwt-signing-private-key notification-private-key" \
              "identity:false:cache-password database-auth-connection-string database-characters-1-connection-string game-auth-host-key jwt-signing-private-key notification-private-key" \
              "commerce:true:cache-password database-auth-connection-string jwt-signing-key" \
              "commerce:false:cache-password database-auth-connection-string"; do
  IFS=: read -r service legacy wanted <<<"$expect"
  issuer=(); [ "$legacy" = true ] || issuer=(--set authentication.issuerSigningKey=)
  keys=$(render "${AUTHENTICATION[@]}" $CACHE "${ESECRETS[@]}" "${issuer[@]}" --set authentication.legacyIssuerSigningKey="$legacy" \
      --set-json "services=[\"$service\"]" --show-only templates/secret.yaml \
    | awk '/^stringData:/{f=1; next} f && /^  [a-z]/{sub(/:.*/, ""); print $1}' | LC_ALL=C sort | tr '\n' ' ')
  [ "$keys" = "$wanted " ] || { echo "$service's ES256 Secret (legacy $legacy) must hold only its keys, not: $keys"; exit 1; }
done
render "${AUTHENTICATION[@]}" $CACHE "${ESECRETS[@]}" --set 'authentication.signingKey= private ' --show-only templates/secret.yaml \
  | grep -q 'jwt-signing-private-key: "private"$'                     || { echo "the private key must reach the Secret, trimmed"; exit 1; }
must_fail "identity without a key id must fail"                     --set existingSecret=x "${W1[@]}" "${ES256[@]}" --set authentication.signingKeyId=
must_fail "a key id that is not a plain name must fail"             --set existingSecret=x "${W1[@]}" "${ES256[@]}" --set 'authentication.signingKeyId=a b'
must_fail "a service without identity and no public key must fail"  --set existingSecret=x "${W1[@]}" --set-json 'services=["worlds"]' --set authentication.legacyIssuerSigningKey=true
must_fail "a private key among the public keys must fail"           --set existingSecret=x "${W1[@]}" "${ES256[@]}" --set-string 'authentication.validationKeys.2026-10=-----BEGIN PRIV''ATE KEY-----'
must_fail "an inline private key with existingSecret must fail"     --set existingSecret=x "${W1[@]}" "${ES256[@]}" --set authentication.signingKey=private
must_fail "an inline host key with existingSecret must fail"        --set existingSecret=x "${W1[@]}" "${ES256[@]}" --set gameAuth.hostKey=host
must_fail "identity's chart-managed Secret without its private key must fail" "${ESECRETS[@]}" --set authentication.signingKey=
must_fail "the HS256 key without legacyIssuerSigningKey must fail"  "${ESECRETS[@]}" --set authentication.legacyIssuerSigningKey=false
must_fail "no host key and no HS256 key to fall back to must fail"  "${ESECRETS[@]}" --set authentication.legacyIssuerSigningKey=false --set authentication.issuerSigningKey= --set gameAuth.hostKey=
must_fail "a legacy flag that is not a boolean must fail"           --set existingSecret=x "${W1[@]}" "${ES256[@]}" --set-string authentication.legacyIssuerSigningKey=yes
# A service's values are required only where it runs: distribution starts from the auth string and the
# signing key alone, with no world, cache, store or game admission values.
helm template t . --set existingSecret=x --set-json 'services=["distribution"]' >/dev/null || { echo "distribution alone must not need other services' values"; exit 1; }
must_fail "an empty services list outside a routes release must fail" --set existingSecret=x "${W1[@]}" --set-json 'services=[]'
must_fail "an unknown service must fail"                              --set existingSecret=x "${W1[@]}" --set-json 'services=["billing"]'

# A routes release renders the IngressRoute alone (design D7.2): one route per manifest rule, a deeper
# rule outranking a shallower one, the default at the bottom and the rollout overrides on top, each
# on both hosts, through the Middleware, to the Service of the rule's service.
ROUTES=(--values ci/routes-values.yaml)
routes=$(render "${ROUTES[@]}")
[ "$(grep '^kind: ' <<<"$routes")" = "kind: IngressRoute" ] || { echo "a routes release must render the IngressRoute alone"; exit 1; }
table=$(awk '
  /^    - kind: Rule$/ { if (match_) print priority, backend, middleware, match_; match_ = ""; middleware = "" }
  /^      match: / { if (index($0, "match: \"(Host(`avalon.example.test`) || Host(`admin.avalon.example.test`)) && PathRegexp(`") != 7)
                       print "a route without both hosts: " $0
                     match_ = $0; sub(/.*PathRegexp\(`/, "", match_); sub(/`\)"$/, "", match_) }
  /^      priority: / { priority = $2 }
  /^      (middlewares|services):$/ { list = $1 }
  /^        - name: / { if (list == "middlewares:") middleware = middleware $3; else backend = $3 }
  /^          port: / { backend = backend ":" $2 }
  END { print priority, backend, middleware, match_ }
' <<<"$routes" | LC_ALL=C sort)
diff <(echo "$table") - <<'EOF' || { echo "routes must follow the manifest (design D7.2)"; exit 1; }
10000 api-identity:8080 avalon-strip-api ^/api(/|$)
10010 api-identity:8080 avalon-strip-api ^/api/(?i)account(/|$)
10010 api-identity:8080 avalon-strip-api ^/api/(?i)game(/|$)
10010 api-identity:8080 avalon-strip-api ^/api/(?i)mfa(/|$)
10010 api-identity:8080 avalon-strip-api ^/api/(?i)notification(/|$)
10010 api-identity:8080 avalon-strip-api ^/api/(?i)pat(/|$)
10010 api-worlds:8080 avalon-strip-api ^/api/(?i)balance(/|$)
10010 api-worlds:8080 avalon-strip-api ^/api/(?i)character(/|$)
10010 api-worlds:8080 avalon-strip-api ^/api/(?i)observability(/|$)
10010 api-worlds:8080 avalon-strip-api ^/api/(?i)public(/|$)
10010 api-worlds:8080 avalon-strip-api ^/api/(?i)world(/|$)
10020 api-commerce:8080 avalon-strip-api ^/api/(?i)account/game-license(/|$)
10020 api-commerce:8080 avalon-strip-api ^/api/(?i)account/purchases(/|$)
10020 api-commerce:8080 avalon-strip-api ^/api/(?i)admin/purchases(/|$)
10020 api-commerce:8080 avalon-strip-api ^/api/(?i)payments/notifications(/|$)
10020 api-distribution:8080 avalon-strip-api ^/api/(?i)client/changelog(/|$)
10020 api-distribution:8080 avalon-strip-api ^/api/(?i)client/channels(/|$)
10020 api-distribution:8080 avalon-strip-api ^/api/(?i)client/launcher(/|$)
10020 api-distribution:8080 avalon-strip-api ^/api/(?i)client/releases(/|$)
10020 api-identity:8080 avalon-strip-api ^/api/(?i)client/auth(/|$)
20020 api-worlds-next:8080 avalon-strip-api ^/api/(?i)world/1(/|$)
20030 api-distribution-next:8080 avalon-strip-api ^/api/(?i)client/channels/dev(/|$)
EOF
helm lint . --quiet "${ROUTES[@]}" >/dev/null || { echo "helm lint failed for the routes release"; exit 1; }
# An override that would send another service's paths, or an internal one, somewhere else refuses to
# render; so does turning routes on in a release that runs services, which would remove its pods.
must_fail_routes() { local why=$1; shift; if helm template t . "${ROUTES[@]}" "$@" >/dev/null 2>&1; then echo "$why"; exit 1; fi; }
must_fail_routes "an override of a path another service owns must fail" --set-json 'routes.overrides=[{"path":"/account/purchases","service":"identity","backend":"x"}]'
must_fail_routes "an override over another service's rules must fail"  --set-json 'routes.overrides=[{"path":"/account","service":"identity","backend":"x"}]'
must_fail_routes "an override of an internal path must fail"           --set-json 'routes.overrides=[{"path":"/internal/game","service":"identity","backend":"x"}]'
must_fail_routes "a missing backend must fail"                         --set routes.backends.commerce=null
must_fail_routes "routes with a service listed must fail"              --set-json 'services=["worlds"]'
must_fail "routes in a release that leaves services out must fail" --set existingSecret=x "${W1[@]}" --set routes.enabled=true --set-json 'routes.hosts=["a.example.test"]'

# The NetworkPolicy (off by default) refuses to render a port with no peer, which would admit every source.
POLICY=(--set networkPolicy.enabled=true --set-json 'networkPolicy.ingressController=[{"podSelector":{"matchLabels":{"app":"traefik"}}}]')
policy=$(render "${HOMELAB[@]}" --set-json 'services=["identity"]' "${POLICY[@]}" --set-json 'networkPolicy.worldServers=[{"podSelector":{"matchLabels":{"app":"world"}}}]' --show-only templates/networkpolicy.yaml)
awk '/^    - from:/ { rule++ } /app: world/ { peer = rule } /port: 9443/ { port = rule } END { exit !(peer && peer == port) }' <<<"$policy" \
                                                                          || { echo "game admission must admit the world servers"; exit 1; }
! grep -q "kind: NetworkPolicy" <<<"$mono"                               || { echo "the NetworkPolicy must be off by default"; exit 1; }
must_fail "a policy with no world servers must fail where identity runs" "${HOMELAB[@]}" --set-json 'services=["identity"]' "${POLICY[@]}"
must_fail "a policy with no ingress controller must fail"                "${HOMELAB[@]}" --set-json 'services=["worlds"]' --set networkPolicy.enabled=true
echo "avalon-api chart OK"
