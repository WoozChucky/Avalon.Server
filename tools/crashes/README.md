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

## Worker symbol resolver

`CrashSymbolResolver` resolves the dump's exact runtime image/debug identity before
best-effort system symbols. It uses the locked `dump_syms --inlines --check-cfi
<adjacent-pair>/runtime.pdb` invocation through `ICrashToolRunner`; tools never
execute downloaded images. Original keys preserve their source case and timestamp,
image-size and GUID/age formatting. Only converted symbols are uploaded to
`symbols-sym`; the `symbols` source bucket remains read-only.

Register one `CrashWorkspace`, `SymbolCache`, `PublicSymbolClient` and
`CrashSymbolResolver` per worker. The reusable local cache must be a dedicated
per-worker directory disjoint from the shared scratch root, not a shared volume
between worker processes. Cache entries verify full source identity, converter and
cache versions, byte length, SHA-256, MODULE, useful FUNC and x64 CFI records.
Publication uses temporary files and atomic renames; incomplete/mismatched pairs
are misses. Metadata and symbol files are evicted as one LRU entry within the
4 GiB cap. A PostgreSQL transaction advisory lock uses a separate context/connection
and the full module/cache identity; the resolver rechecks local and remote caches
under that lock. Local completed symbols survive remote upload failure; later local
cache demand retries the upload and exposes `CacheUploadFailed` for telemetry.

The caller supplies its eight-minute attempt cancellation token and one
`SymbolDownloadBudget`, charged for the already downloaded crash archive. The same
budget follows all private/public/converted downloads and retries. Each network
operation has one linked 30-second deadline spanning SDK retries/redirects and
response-body streaming. Source artifacts are capped at 256 MiB and cumulative
attempt downloads at 1 GiB. All conversion leases use the same scratch manager and
2 GiB quota as the main attempt. A failed converter deletes only its own lease;
the caller still owns the main report lease through parsing and disposal.

The 60-second optional-symbol phase includes cache lookups, advisory-lock waits,
HTTP downloads, pair validation, conversion, materialization and upload, with at
most 16 module attempts. Its deadline cancels native work through the runner's
process-tree cleanup. The required runtime phase is outside that budget. Missing
optional symbols set `MissingSystemSymbols` and preserve module/range identities.
The v1 public allowlist pairs matching DLL/PDB stems for `ntdll`, `kernel32`,
`kernelbase`, `ucrtbase`, `vcruntime140`, `vcruntime140_1` and `msvcp140`. Runtime
names and runtime debug identities are excluded even when paired with a system
module name. No Vulkan loader or GPU-driver publication is assumed.

The default public origin is `https://msdl.microsoft.com/download/symbols/`.
Redirects are manual, capped at three, and stay at that origin or explicitly
configured HTTPS redirect origins (none by default). Automatic decompression,
cookies and credentials are disabled. CAB/SZDD compressed files and encoded HTTP
responses are deliberately unsupported: no decompression is attempted. An ordinary
404, unsupported compression or invalid pair is a 24-hour negative cache entry;
transient errors use one-minute backoff. The adapter does not probe underscore
compressed filenames after a 404. This negative cache is process-local, capped at
4,096 entries, and is lost on restart or capacity eviction.

`CrashSymbolResolverNativeShould` is the opt-in protected-fixture acceptance gate.
It requires Linux, `AVALON_CRASHES_INTEGRATION=1`,
`AVALON_CRASHES_PRIVATE_PAIR` and `AVALON_CRASHES_PRIVATE_FIXTURE`; missing inputs
fail in explicit integration mode. It invokes the actual resolver and pinned tools,
then requires `Application.cpp`/`main.cpp` source lines, CFI and inline entries.
It never publishes raw pairs, dumps or raw stackwalk output.
