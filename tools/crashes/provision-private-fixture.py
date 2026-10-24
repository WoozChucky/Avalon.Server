"""Protected main-only ephemeral CI provisioning. URLs/credentials never enter argv or logs."""
import hashlib
import os
from pathlib import Path
import re
import shutil
import sys
import tarfile
import urllib.parse
import urllib.request

from verify_private_fixture import verify

base = Path(os.environ["RUNNER_TEMP"]).resolve()
root = base / "crashes-private"
if root.parent != base or root.is_symlink():
    raise SystemExit("Invalid protected fixture destination")
if sys.argv[1:] == ["--cleanup"]:
    if root.exists():
        shutil.rmtree(root)
    raise SystemExit(0)
if sys.argv[1:]:
    raise SystemExit("Unexpected provisioning argument")
if os.environ.get("GITHUB_EVENT_NAME") != "workflow_dispatch" or os.environ.get("GITHUB_REF") != "refs/heads/main":
    raise SystemExit("Private fixtures require a trusted main workflow dispatch")
url = os.environ.get("CRASHES_FIXTURE_URL", "")
digest = os.environ.get("CRASHES_FIXTURE_SHA256", "")
if urllib.parse.urlsplit(url).scheme != "https" or not re.fullmatch(r"[0-9a-f]{64}", digest):
    raise SystemExit("Protected HTTPS fixture URL and SHA256 prerequisites missing")
# Exact pair plus controlled dump; no arbitrary archive paths, links, devices or executables outside pair.
required = {"pair/runtime.exe", "pair/runtime.pdb", "fixture/dump.dmp"}
allowed = required | {"fixture/meta.json", "fixture/log-tail.txt", "fixture/provenance.json", "fixture/runtime.sym"}
os.umask(0o077)
root.mkdir(exist_ok=False)
archive = root / "private.tar.gz"
try:
    # No redirects: a signed credential must never be forwarded to another origin.
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, req, fp, code, msg, headers, newurl):
            return None

    sha = hashlib.sha256()
    total = 0
    with urllib.request.build_opener(NoRedirect()).open(url, timeout=60) as response, archive.open("wb") as output:
        while chunk := response.read(1024 * 1024):
            total += len(chunk)
            if total > 512 * 1024 * 1024:
                raise ValueError("Archive exceeds cap")
            sha.update(chunk)
            output.write(chunk)
    if sha.hexdigest() != digest:
        raise ValueError("Archive digest mismatch")
    with tarfile.open(archive, "r:gz") as source:
        members = source.getmembers()
        names = [m.name for m in members]
        if len(names) != len(set(names)) or not required.issubset(names):
            raise ValueError("Invalid archive layout")
        if any(not m.isfile() or m.name not in allowed or m.size <= 0 for m in members):
            raise ValueError("Unexpected archive member")
        if sum(m.size for m in members) > 1024 * 1024 * 1024:
            raise ValueError("Expanded archive exceeds cap")
        source.extractall(root, filter="data")
    verify(root / "pair", root / "fixture")
    archive.unlink()
    # Docker's nonroot UID can read only these fixture directories; host parent stays private.
    for path in root.rglob("*"):
        path.chmod(0o755 if path.is_dir() else 0o644)
except Exception:
    shutil.rmtree(root)
    raise SystemExit("Protected fixture provisioning failed (details suppressed to protect credentials)") from None
