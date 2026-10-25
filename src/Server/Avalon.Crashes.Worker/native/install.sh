#!/usr/bin/env bash
set -euo pipefail
if [[ $# != 1 || "$1" != /* ]]; then
    echo 'usage: install.sh <absolute-output-directory>' >&2
    exit 1
fi
[[ "$(uname -s)" == Linux && "$(uname -m)" == x86_64 ]] || {
    echo 'Linux x86_64 required' >&2; exit 1;
}
directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
python3 - "$directory/tools.lock.json" "$1" <<'PY'
import hashlib
import json
from pathlib import Path
import shutil
import sys
import tarfile
import tempfile
import urllib.request

lock, output = Path(sys.argv[1]), Path(sys.argv[2])
output.mkdir(parents=True, exist_ok=True)
for tool in json.loads(lock.read_text())["tools"]:
    with tempfile.TemporaryDirectory(prefix="avalon-tool-") as temp:
        archive = Path(temp) / "tool.tar.xz"
        request = urllib.request.Request(tool["url"], headers={"User-Agent": "Avalon-native-tools"})
        with urllib.request.urlopen(request, timeout=30) as response, archive.open("wb") as target:
            shutil.copyfileobj(response, target)
        if hashlib.sha256(archive.read_bytes()).hexdigest() != tool["archiveSha256"]:
            raise SystemExit("native tool archive digest mismatch")
        with tarfile.open(archive) as package:
            # Extract only locked regular files; no archive links or arbitrary paths.
            for name in [tool["name"], *tool["licenses"]]:
                member = package.getmember(tool["archiveDirectory"] + "/" + name)
                if not member.isfile():
                    raise SystemExit("native tool archive member is not a regular file")
                data = package.extractfile(member).read()
                if name == tool["name"]:
                    if hashlib.sha256(data).hexdigest() != tool["binarySha256"]:
                        raise SystemExit("native tool binary digest mismatch")
                    destination = output / name
                else:
                    destination = output / "licenses" / tool["name"] / name
                destination.parent.mkdir(parents=True, exist_ok=True)
                destination.write_bytes(data)
                if name == tool["name"]:
                    destination.chmod(0o755)
shutil.copyfile(lock, output / "tools.lock.json")
print("Installed locked Linux native tools and licenses")
PY
