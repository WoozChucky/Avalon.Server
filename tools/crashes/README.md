# Native crash-analysis acceptance

This gate proves actual Windows x64 optimized Release PE/PDB conversion and dump
walking on Linux. It uses Mozilla `dump_syms` 2.3.9 and rust-minidump
`minidump-stackwalk` 0.27.0. [The lock](../../src/Server/Avalon.Crashes.Worker/native/tools.lock.json)
records upstream release URLs, full source revisions, features, archive/binary
SHA-256 values and licenses. The installer downloads the released binaries,
checks both digests and installs their license files; no source build or mutable
upstream installer is involved. The image currently supports Linux x86_64 only.

## Protected fixture

Only [safe provenance](../../tests/Fixtures/Crashes/release-access-violation/provenance.json)
is committed. Provision these exact protected files before running acceptance:

- `out/crashes/fixtures/release-access-violation/{dump.dmp,runtime.sym,provenance.json}`
- An operator-owned private directory containing adjacent `runtime.exe` and
  `runtime.pdb` from the recorded build; pass its absolute path as `PRIVATE_PAIR`.

`out/` is ignored. Do not upload the dump, original executable/PDB, raw stackwalk
JSON or protected directories as CI artifacts. The sanitized `.sym` retains
inline/CFI records while reducing `FILE` paths and the `INFO CODE_ID` image name
to basenames. SHA-256 values for all four files are in provenance. For this proof,
the private pair was copied from an existing Release build, never rebuilt or
modified. The embedded client revision is authoritative; the checkout at capture
was newer. Flags were inspected in its generated Release project and CMake cache.

Capture used `runtime.exe --channel dev --no-log-file --crash access-violation`
with fresh isolated `LOCALAPPDATA` and `APPDATA`, before any account or network
session. Exit was `-1073741819` (`0xC0000005`). Binary review found a normal
minidump with thread/context/stack memory, module paths and process information:
86,705 bytes, 62 absolute-path strings, zero credential-marker strings. Absence of
marker strings does not certify arbitrary memory safe. This is the approved
controlled-private-fixture fallback; public CI cannot run native acceptance
without explicitly provisioning the private files.

## Run

From the repository root, using a Linux-compatible Docker CLI shell:

```bash
docker build -f src/Server/Avalon.Crashes.Worker/native/Dockerfile \
  -t avalon-crashes-native:task1 .

# Exact PE/PDB/dump identities, Linux conversion, generated and cached walking.
docker run --rm --network none --read-only --cap-drop ALL \
  --security-opt no-new-privileges \
  --tmpfs /tmp:rw,noexec,nosuid,size=512m \
  -v "$PWD:/repo:ro" -v "$PRIVATE_PAIR:/private-pair:ro" \
  avalon-crashes-native:task1 bash tools/crashes/verify-native.sh \
  --fixture out/crashes/fixtures/release-access-violation \
  --private-pair /private-pair

# Cached sanitized symbols alone; no original executable/PDB mounted.
docker run --rm --network none --read-only --cap-drop ALL \
  --security-opt no-new-privileges \
  --tmpfs /tmp:rw,noexec,nosuid,size=512m -v "$PWD:/repo:ro" \
  avalon-crashes-native:task1 bash tools/crashes/verify-native.sh \
  --fixture out/crashes/fixtures/release-access-violation
```

Both runs must exit zero with:

```text
PASS: MODULE, unwind, fault/thread, Application.cpp/main.cpp lines, CFI and wrong-symbol rejection
```

On Linux with the tools installed, the equivalent interface is
`bash tools/crashes/verify-native.sh --fixture <directory> [--private-pair <directory>]`.
`bash src/Server/Avalon.Crashes.Worker/native/install.sh /absolute/tools-directory`
installs the same locked tools; add that directory to `PATH`. Python 3.12+, Bash
and an x86_64 Linux GNU runtime are prerequisites. Missing tools/fixtures and
changed tool binaries fail; no native checks are silently skipped. The plain
committed fixture path fails with `FAIL: missing fixture: dump.dmp` until private
material is provisioned. The container runs UID/GID 65532; mounts must be readable
by that user. Native child processes receive only PATH, HOME and LANG. No remote
symbol URLs are supplied, and runtime network access is disabled by Docker.

## Evidence recorded on 2026-10-23

The dump and both original files agree on debug ID
`E4B4BCD3AA284519805DCBC59F3D27C112`, image timestamp `6ADBA341` and
image size `c34000`. PDB GUID/age, PE CodeView and minidump module identities
are independently read from binary structures before conversion.

| Crashing-thread frame | Line | Recovery trust |
| --- | ---: | --- |
| `CrashHandler.cpp` | 240 | `context` |
| `Application.cpp` | 224 | `cfi` |
| `main.cpp` | 194 | `cfi` |

Fault is `EXCEPTION_ACCESS_VIOLATION_WRITE`, crashing-thread index 0, dump thread
ID 23952. One crashing-thread inline entry survives. Conversion used adjacent
validated `runtime.exe`/`runtime.pdb`, `--inlines --check-cfi`, producing 15,292
functions, 22,842 `STACK CFI INIT`, 168,000 `INLINE` and 35,072 `INLINE_ORIGIN`
records. Both generated and sanitized cached symbols reproduce the source frames.

Every successful run repeats the real stackwalk with deliberately source-less
symbols of the same MODULE identity and requires the source gate to fail. An
additional acceptance run with all `FILE` records removed (retaining functions,
CFI, inline records and correct identity, with its test digest updated) exited 1:
`FAIL: source-resolution gate failed: Application.cpp`. No successful stack data
is fabricated. This native proof does not establish persistence, grouping,
retention or public-system-symbol behavior; later tasks test those separately.
