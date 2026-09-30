"""Remove old dev and nightly GHCR versions while preserving releases and recent builds.

Run ``python registry_cleanup.py [--dry-run]`` with GH_TOKEN set.
"""

import argparse
import json
import re
import subprocess
import urllib.parse
from datetime import datetime, timedelta, timezone


OWNER = "WoozChucky"
PACKAGES = (
    "avalon-server/world", "avalon-server/auth", "avalon-server/api", "avalon-server/balance",
    "charts/avalon-world", "charts/avalon-auth", "charts/avalon-api", "charts/avalon-balance",
)
PRERELEASE = re.compile(r"^\d+\.\d+\.\d+-(dev|nightly)\.\d+$")


def select_deletions(versions: list[dict], now: datetime, keep: int = 10,
                     max_age: timedelta = timedelta(days=14)) -> list[int]:
    """Select versions older than max_age outside the newest keep of either kind."""
    newest: set[int] = set()
    for kind in ("dev", "nightly"):
        candidates = (v for v in versions if any(
            (match := PRERELEASE.fullmatch(tag)) and match[1] == kind for tag in v["tags"]
        ))
        newest.update(v["id"] for v in sorted(
            candidates, key=lambda v: (v["created_at"], v["id"]), reverse=True
        )[:keep])

    doomed = []
    for version in versions:
        tags = version["tags"]
        if not tags or not all(PRERELEASE.fullmatch(tag) for tag in tags):
            continue
        if version["id"] in newest or now - version["created_at"] <= max_age:
            continue
        doomed.append(version["id"])
    return doomed


def _gh(*args: str) -> str:
    return subprocess.run(["gh", "api", *args], check=True, capture_output=True, text=True).stdout


def _versions(path: str) -> list[dict]:
    """Read concatenated JSON pages emitted by ``gh api --paginate``."""
    output = _gh("--paginate", path)
    decoder = json.JSONDecoder()
    versions = []
    offset = 0
    while offset < len(output):
        remaining = output[offset:].lstrip()
        if not remaining:
            break
        offset = len(output) - len(remaining)
        page, consumed = decoder.raw_decode(remaining)
        versions.extend(page)
        offset += consumed
    return [{
        "id": v["id"],
        "created_at": datetime.fromisoformat(v["created_at"].replace("Z", "+00:00")),
        "tags": v["metadata"]["container"]["tags"],
    } for v in versions]


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args(argv)
    now = datetime.now(timezone.utc)
    for package in PACKAGES:
        path = f"/users/{OWNER}/packages/container/{urllib.parse.quote(package, safe='')}/versions?per_page=100"
        versions = _versions(path)
        for version_id in select_deletions(versions, now):
            tags = next(v["tags"] for v in versions if v["id"] == version_id)
            print(f"{'would delete' if args.dry_run else 'deleting'} {package} {', '.join(tags)}", flush=True)
            if not args.dry_run:
                _gh("-X", "DELETE", path.split("?", 1)[0] + f"/{version_id}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
