#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export MSYS_NO_PATHCONV=1
helm lint . -f ci/test-values.yaml
out=$(helm template t . -f ci/test-values.yaml)
printf '%s' "$out" | python -c '
import sys,yaml
items=list(yaml.safe_load_all(sys.stdin))
def one(kind):
    matches=[x for x in items if x and x["kind"]==kind]
    assert len(matches)==1,(kind,len(matches))
    return matches[0]
d=one("Deployment"); p=d["spec"]["template"]["spec"]; c=p["containers"][0]
assert p["nodeSelector"]=={"kubernetes.io/os":"linux","kubernetes.io/arch":"amd64"}
assert p.get("automountServiceAccountToken")==False,"worker mounts service account token"
assert p["securityContext"]["runAsUser"]==1654,"worker must use .NET non-root UID"
assert p["securityContext"]["runAsNonRoot"] and p["securityContext"]["seccompProfile"]["type"]=="RuntimeDefault"
assert c["securityContext"]==dict(allowPrivilegeEscalation=False,readOnlyRootFilesystem=True,capabilities=dict(drop=["ALL"]))
assert d["spec"]["replicas"]==1 and d["spec"]["strategy"]["type"]=="Recreate"
assert c["livenessProbe"]["httpGet"]["path"]=="/alive" and c["readinessProbe"]["httpGet"]["path"]=="/health"
assert c["startupProbe"]["httpGet"]["path"]=="/alive"
assert c["resources"]==dict(requests={"cpu":"100m","memory":"256Mi","ephemeral-storage":"6Gi"},limits={"cpu":"1","memory":"1Gi","ephemeral-storage":"7Gi"})
assert c["image"]=="ghcr.io/woozchucky/avalon-server/crashes:0.0.0-dev"
assert c["args"]==["run"]
volumes={v["name"]:v["emptyDir"]["sizeLimit"] for v in p["volumes"]}
assert volumes=={"scratch":"2Gi","symbols":"4Gi"}
mounts={v["name"]:v["mountPath"] for v in c["volumeMounts"]}
assert mounts=={"scratch":"/scratch","symbols":"/symbols"}
env={e["name"]:e for e in c["env"]}
for name,key in [("Database__Crashes__ConnectionString","database-crashes-connection-string"),("Crashes__Storage__AccessKeyId","crash-storage-access-key"),("Crashes__Storage__SecretAccessKey","crash-storage-secret-key")]:
    assert env[name]["valueFrom"]["secretKeyRef"]==dict(name="crashes-worker-test",key=key)
assert env["Crashes__Processing__ScratchRoot"]["value"]=="/scratch/reports"
assert env["Crashes__Symbols__CacheRoot"]["value"]=="/symbols/cache"
assert env["Crashes__Processing__ScratchLimitBytes"]["value"]=="2147483648"
assert env["Crashes__Symbols__LocalCacheBytes"]["value"]=="4294967296"
assert env["Crashes__Storage__Bucket"]["value"]=="crashes"
assert env["Crashes__Symbols__OriginalBucket"]["value"]=="symbols"
assert env["Crashes__Symbols__ConvertedBucket"]["value"]=="symbols-sym"
assert not any("Auth" in name or "Signing" in name for name in env)
j=one("CronJob"); jp=j["spec"]["jobTemplate"]["spec"]["template"]["spec"]; jc=jp["containers"][0]
assert j["spec"]["schedule"]=="0 0 * * *" and j["spec"]["timeZone"]=="Etc/UTC" and j["spec"]["concurrencyPolicy"]=="Forbid"
assert jp["nodeSelector"]==p["nodeSelector"]
assert jc["args"]==["cleanup"]
assert jc["resources"]==dict(requests={"cpu":"100m","memory":"256Mi","ephemeral-storage":"16Mi"},limits={"cpu":"1","memory":"1Gi","ephemeral-storage":"64Mi"})
assert [e["name"] for e in jc["env"]]==["Database__Crashes__ConnectionString","DOTNET_EnableDiagnostics"]
assert not jp["automountServiceAccountToken"] and jp["securityContext"]==p["securityContext"]
assert jc["securityContext"]==c["securityContext"] and "volumeMounts" not in jc
assert one("Service")["spec"]["type"]=="ClusterIP"
assert not any(x["kind"] in ["Ingress","IngressRoute","Secret","PersistentVolumeClaim"] for x in items if x)
policies=[x for x in items if x and x["kind"]=="NetworkPolicy"]
assert len(policies)==2
cleanup=next(x for x in policies if x["spec"]["podSelector"]["matchLabels"]["app.kubernetes.io/component"]=="cleanup")
assert cleanup["spec"]["ingress"]==[]
assert len(cleanup["spec"]["egress"])==2,"cleanup permits more than PostgreSQL and DNS"
assert {port["port"] for rule in cleanup["spec"]["egress"] for port in rule["ports"]}=={5432,53}
print("worker chart isolation checks passed")
'
paused=$(helm template t . -f ci/test-values.yaml --set paused=true)
grep -q -- '--paused' <<<"$paused"
for setting in replicaCount=2 retentionDays=91 storage.bucket=symbols symbols.convertedBucket=symbols storage.endpoint=http://garage.example.test; do
  if helm template t . -f ci/test-values.yaml --set "$setting" >/dev/null 2>&1; then echo "unsafe setting accepted: $setting"; exit 1; fi
done
# An optional product-image gate, after project-local publish and docker build.
if [[ -n "${CRASHES_IMAGE:-}" ]]; then
  image_manifest=$(docker run --rm --read-only --cap-drop ALL --security-opt no-new-privileges --network none --entrypoint /bin/cat "$CRASHES_IMAGE" /opt/avalon-native/tools.lock.json)
  printf %s "$image_manifest" | python -c 'import json,sys; assert json.load(sys.stdin)==json.load(open("../../native/tools.lock.json"))'

  docker run --rm --read-only --cap-drop ALL --security-opt no-new-privileges --network none --entrypoint /bin/sh "$CRASHES_IMAGE" -ec '
    test "$(id -u)" = 1654
    test -x /usr/bin/prlimit && test -x /usr/bin/setsid
    /usr/bin/prlimit --cpu=1:1 -- /usr/bin/setsid /bin/true
    test -r /opt/avalon-native/tools.lock.json
    test ! -w /app && test ! -w /opt/avalon-native
  '
  while IFS=$'\t' read -r name version digest; do
    digest=${digest%$'\r'}
    actual=$(docker run --rm --read-only --cap-drop ALL --security-opt no-new-privileges --network none --entrypoint /bin/sh "$CRASHES_IMAGE" -ec 'sha256sum "/opt/avalon-native/$1"; "/opt/avalon-native/$1" --version' sh "$name")
    grep -Fq "$digest  /opt/avalon-native/$name" <<<"$actual"
    grep -Fq "$version" <<<"$actual"
  done < <(python -c 'import json; [print(t["name"],t["version"],t["binarySha256"],sep="\t") for t in json.load(open("../../native/tools.lock.json"))["tools"]]')
  echo "product image UID, immutable tools, pinned digests/versions and Linux limit executables passed"
fi
