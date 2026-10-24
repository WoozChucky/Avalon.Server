#!/usr/bin/env bash
# Private fixtures and credentials never enter an image context or a public test artifact.
set -euo pipefail
export MSYS_NO_PATHCONV=1
fail() { echo "crashes integration: $*" >&2; exit 1; }
[[ $# == 2 && $1 == --suite ]] || fail "usage: $0 --suite postgres|native|garage|all"
suite=$2
case "$suite" in postgres|native|garage|all) ;; *) fail "unknown suite" ;; esac
for tool in docker dotnet python; do command -v "$tool" >/dev/null || fail "missing prerequisite: $tool"; done
docker compose version >/dev/null || fail "Docker Compose v2 is required"
# Only a local daemon is allowed; freeze the selected context for the whole lifecycle.
[[ -z ${DOCKER_HOST:-} && -z ${DOCKER_TLS_VERIFY:-} ]] || fail "refusing Docker host/TLS overrides"
export DOCKER_CONTEXT
DOCKER_CONTEXT=$(docker context show)
endpoint=$(docker context inspect "$DOCKER_CONTEXT" --format '{{.Endpoints.docker.Host}}')
case "$endpoint" in unix:///*|npipe:////./pipe/*) ;; *) fail "integration requires a local Docker daemon" ;; esac
docker info >/dev/null 2>&1 || fail "Docker daemon unavailable"
# Only internally generated endpoints/credentials are accepted, even for localhost.
while IFS= read -r name; do
  case "$name" in COMPOSE_*|AVALON_CRASHES_TEST_POSTGRES|AVALON_CRASHES_TEST_S3_*) fail "refusing supplied database/storage configuration: $name" ;; esac
done < <(compgen -e)
image=${CRASHES_IMAGE:-avalon-crashes-worker:integration}
docker image inspect "$image" >/dev/null 2>&1 || fail "build the product image first (CRASHES_IMAGE)"
root=$(cd "$(dirname "$0")/../.." && { pwd -W 2>/dev/null || pwd; })
native_mounts=()
if [[ $suite == native || $suite == all ]]; then
  [[ -n ${AVALON_CRASHES_PRIVATE_PAIR:-} && -n ${AVALON_CRASHES_PRIVATE_FIXTURE:-} ]] || fail "native suite requires protected AVALON_CRASHES_PRIVATE_PAIR and AVALON_CRASHES_PRIVATE_FIXTURE"
  for file in "$AVALON_CRASHES_PRIVATE_PAIR/runtime.exe" "$AVALON_CRASHES_PRIVATE_PAIR/runtime.pdb" "$AVALON_CRASHES_PRIVATE_FIXTURE/dump.dmp"; do
    [[ -s $file ]] || fail "protected native fixture is incomplete"
  done
  python "$root/tools/crashes/verify_private_fixture.py" || fail "native fixture provenance validation failed"
  native_mounts=(-v "$(cd "$AVALON_CRASHES_PRIVATE_PAIR" && { pwd -W 2>/dev/null || pwd; }):/private-pair:ro"
                 -v "$(cd "$AVALON_CRASHES_PRIVATE_FIXTURE" && { pwd -W 2>/dev/null || pwd; }):/private-fixture:ro")
fi
id=$(python -c 'import uuid; print(uuid.uuid4().hex)')
project=crashes-test-$id
# Work remains under the ignored fixture boundary, outside the worker Docker context.
mkdir -p "$root/out/crashes/integration/$id"
run=$(cd "$root/out/crashes/integration/$id" && { pwd -W 2>/dev/null || pwd; })
export CRASHES_RUN_DIR
CRASHES_RUN_DIR=$(cd "$run" && { pwd -W 2>/dev/null || pwd; })
repo_mount=$(cd "$root" && { pwd -W 2>/dev/null || pwd; })
compose=(docker compose --project-name "$project" -f "$root/tools/crashes/compose.integration.yml")
runner=$project-runner
results=$project-results
containers=()
compose_started=false
cleanup() {
  local result=$?
  trap - EXIT
  for container in "${containers[@]}"; do
    if label=$(docker inspect "$container" --format '{{index .Config.Labels "avalon.crashes.integration"}}' 2>/dev/null); then
      if [[ $label == "$id" ]]; then docker rm -f "$container" >/dev/null || result=1; else result=1; fi
    fi
  done
  if [[ $compose_started == true ]]; then "${compose[@]}" down --volumes --remove-orphans >/dev/null 2>&1 || result=1; fi
  for volume in "$runner" "$results"; do
    if label=$(docker volume inspect "$volume" --format '{{index .Labels "avalon.crashes.integration"}}' 2>/dev/null); then
      if [[ $label == "$id" ]]; then docker volume rm "$volume" >/dev/null || result=1; else result=1; fi
    fi
  done
  # Refuse any computed recursive deletion outside this exact generated work directory.
  python - "$run" "$root/out/crashes/integration" "$id" <<'PY'
import pathlib, shutil, sys
path, parent = (pathlib.Path(p).resolve() for p in sys.argv[1:3])
assert path.parent == parent and path.name == sys.argv[3] and len(path.name) == 32
shutil.rmtree(path)
PY
  exit "$result"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
umask 077
python - "$run" "$id" <<'PY'
import pathlib, secrets, sys
p=pathlib.Path(sys.argv[1]); password=secrets.token_hex(32)
(p/'postgres.env').write_text('POSTGRES_PASSWORD='+password+'\n')
(p/'tests.env').write_text('AVALON_CRASHES_INTEGRATION=1\nAVALON_CRASHES_TEST_POSTGRES=Host=127.0.0.1;Port=5433;Database=postgres;Username=postgres;Password='+password+'\nAVALON_CRASHES_TEST_RUNNER=/runner/vstest.console.dll\nAVALON_CRASHES_PRIVATE_PAIR=/private-pair\nAVALON_CRASHES_PRIVATE_FIXTURE=/private-fixture\nSSL_CERT_FILE=/tls/server.crt\nTMPDIR=/scratch\n')
(p/'garage.toml').write_text('metadata_dir = "/var/lib/garage/meta"\ndata_dir = "/var/lib/garage/data"\ndb_engine = "sqlite"\nreplication_factor = 1\nrpc_bind_addr = "[::]:3901"\nrpc_public_addr = "127.0.0.1:3901"\nrpc_secret = "'+secrets.token_hex(32)+'"\n[s3_api]\ns3_region = "garage"\napi_bind_addr = "[::]:3900"\n')
(p/'nginx.conf').write_text('server { listen 8443 ssl; ssl_certificate /tls/server.crt; ssl_certificate_key /tls/server.key; client_max_body_size 4m; location / { proxy_pass http://garage:3900; proxy_set_header Host $http_host; proxy_request_buffering off; } }\n')
(p/'tls').mkdir()
PY
# This exact SDK's test runner targets the same .NET10.0.13 runtime as the pinned product image.
sdk=mcr.microsoft.com/dotnet/sdk:10.0@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa
docker run --rm --network none -v "$CRASHES_RUN_DIR/tls:/tls" --entrypoint sh "$sdk" -c 'openssl req -x509 -newkey rsa:2048 -nodes -days 1 -keyout /tls/server.key -out /tls/server.crt -subj /CN=garage-tls -addext subjectAltName=DNS:garage-tls >/dev/null 2>&1; chmod 755 /tls; chmod 644 /tls/server.crt' || fail "TLS certificate generation failed"
dotnet publish "$root/tests/Avalon.Crashes.IntegrationTests/Avalon.Crashes.IntegrationTests.csproj" -c Release -r linux-x64 --self-contained false -p:UseAppHost=false -p:RuntimeFrameworkVersion=10.0.13 -o "$run/tests" >"$run/build.log" 2>&1 || { cat "$run/build.log"; fail "integration test publish failed"; }
chmod -R a+rX "$run/tests"
docker volume create --label "avalon.crashes.integration=$id" "$runner" >/dev/null
docker volume create --label "avalon.crashes.integration=$id" "$results" >/dev/null
docker run --rm --network none -v "$runner:/runner" -v "$results:/results" --entrypoint sh "$sdk" -c 'cp -a /usr/share/dotnet/sdk/10.0.402/. /runner/; chown 1654:1654 /results; chmod 700 /results'
network=none
if [[ $suite != native ]]; then
  compose_started=true
  "${compose[@]}" up -d --wait postgres >/dev/null
  pg=$("${compose[@]}" ps -q postgres)
  network=container:$pg
fi
if [[ $suite == garage || $suite == all ]]; then
  "${compose[@]}" up -d garage garage-tls >/dev/null
  ready=false
  for ((attempt=0; attempt<60; attempt++)); do
    if "${compose[@]}" exec -T garage /garage status >"$run/garage-status" 2>/dev/null; then ready=true; break; fi
    sleep 1
  done
  [[ $ready == true ]] || fail "Garage did not become ready"
  node=$(python - "$run/garage-status" <<'PY'
import re, sys
text=open(sys.argv[1]).read()
match=re.search(r'^([a-f0-9]{16,64})\s', text, re.M)
if not match: raise SystemExit('Garage node ID missing')
print(match[1])
PY
)
  "${compose[@]}" exec -T garage /garage layout assign -z test -c 1G "$node" >"$run/garage-layout" 2>&1
  "${compose[@]}" exec -T garage /garage layout apply --version 1 >>"$run/garage-layout" 2>&1
  "${compose[@]}" exec -T garage /garage key create "$project" >"$run/garage-key" 2>&1
  key=$(python - "$run" "$id" <<'PY'
import pathlib, re, sys
p=pathlib.Path(sys.argv[1]); text=(p/'garage-key').read_text()
key=re.search(r'Key ID:\s*(\S+)',text)[1]; secret=re.search(r'Secret key:\s*(\S+)',text)[1]
with (p/'tests.env').open('a') as f:
    f.write('AVALON_CRASHES_TEST_S3_ENDPOINT=https://garage-tls:8443\nAVALON_CRASHES_TEST_S3_BUCKET=crashes-test-'+sys.argv[2]+'\nAVALON_CRASHES_TEST_S3_ACCESS_KEY='+key+'\nAVALON_CRASHES_TEST_S3_SECRET_KEY='+secret+'\n')
print(key)
PY
)
  "${compose[@]}" exec -T garage /garage bucket create "$project" >"$run/garage-bucket" 2>&1
  "${compose[@]}" exec -T garage /garage bucket allow --read --write --key "$key" "$project" >>"$run/garage-bucket" 2>&1
fi
suites=("$suite")
[[ $suite != all ]] || suites=(postgres garage native)
for selected in "${suites[@]}"; do
  case "$selected" in postgres) trait=CrashesPostgres ;; garage) trait=CrashesGarage ;; native) trait=CrashesNative ;; esac
  container=$project-$selected
  containers+=("$container")
  mounts=()
  selected_network=$network
  if [[ $selected == native ]]; then mounts=("${native_mounts[@]}"); selected_network=none; fi
  result=0
  docker run --name "$container" --label "avalon.crashes.integration=$id" --read-only --cap-drop ALL --security-opt no-new-privileges --network "$selected_network" --memory 1g --cpus 1 \
    --tmpfs /scratch:rw,nosuid,size=2g,uid=0,gid=1654,mode=2770 --env-file "$run/tests.env" \
    -v "$CRASHES_RUN_DIR/tests:/tests:ro" -v "$CRASHES_RUN_DIR/tls/server.crt:/tls/server.crt:ro" -v "$runner:/runner:ro" -v "$results:/results" \
    -v "$repo_mount/tools/crashes/test-process.sh:/repo/tools/crashes/test-process.sh:ro" "${mounts[@]}" \
    --entrypoint dotnet "$image" exec /runner/vstest.console.dll /tests/Avalon.Crashes.IntegrationTests.dll \
    "--TestCaseFilter:Category=$trait" '--logger:trx;LogFileName=results.trx' "--ResultsDirectory:/results/$selected" >"$run/$selected.log" 2>&1 || result=$?
  docker cp "$container:/results/$selected/results.trx" "$run/$selected.trx" >/dev/null 2>&1 || fail "$selected produced no test results (runner exit $result)"
  python "$root/tools/crashes/check-integration-results.py" "$run/$selected.trx" "$selected" || fail "$selected failed; private logs/results removed during cleanup"
  [[ $result == 0 ]] || fail "$selected runner exited $result"
done
