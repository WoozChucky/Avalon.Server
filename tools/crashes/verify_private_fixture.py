"""Verify protected files against public provenance without emitting their content or paths."""
import hashlib
import json
import os
from pathlib import Path


def verify(pair: Path, fixture: Path) -> None:
    root = Path(__file__).resolve().parents[2]
    expected = json.loads((root / "tests/Fixtures/Crashes/release-access-violation/provenance.json").read_text())["sha256"]
    files = [pair / "runtime.exe", pair / "runtime.pdb", fixture / "dump.dmp"]
    if (fixture / "runtime.sym").exists():
        files.append(fixture / "runtime.sym")
    for path in files:
        if not path.is_file() or path.is_symlink():
            raise ValueError("Protected fixture must contain regular files")
        with path.open("rb") as source:
            actual = hashlib.file_digest(source, "sha256").hexdigest()
        if actual != expected[path.name]:
            raise ValueError("Protected fixture does not match approved provenance")


if __name__ == "__main__":
    try:
        verify(Path(os.environ["AVALON_CRASHES_PRIVATE_PAIR"]), Path(os.environ["AVALON_CRASHES_PRIVATE_FIXTURE"]))
    except Exception:
        raise SystemExit("Protected fixture missing or differs from approved provenance") from None
