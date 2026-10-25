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
Before any symbol I/O, the resolver groups every dump module by its Breakpad
debug-file/debug-ID key using case-insensitive Windows names. Conflicting image
name/ID groups are omitted entirely from the optional symbol tree and availability
list, including conflicts with modules ineligible for public lookup. Load addresses
and diagnostic versions do not distinguish binaries: repeated instances of the
same image/debug identity may share symbols. A conflict involving the required
runtime key raises `InvalidAnalysis` before I/O because safe symbol binding is
impossible; it is an operational analysis failure, not an invalid-archive claim.
The v1 public allowlist pairs matching DLL/PDB stems for `ntdll`, `kernel32`,
`kernelbase`, `ucrtbase`, `vcruntime140`, `vcruntime140_1` and `msvcp140`. Runtime
names and runtime debug identities are excluded even when paired with a system
module name. No Vulkan loader or GPU-driver publication is assumed.

Public-origin policy v1 permits only the fixed
`https://msdl.microsoft.com/download/symbols/` base, documented by
[Microsoft's SymSrv reference](https://learn.microsoft.com/en-us/windows/win32/debug/using-symsrv).
Configuration may select only a subset of the code-owned approved authorities;
v1 contains `https://msdl.microsoft.com` on port 443 and no external CDN authority.
Arbitrary initial hosts, initial paths, redirect hosts and nondefault ports are
configuration errors. Redirects are manual, capped at three, and restricted to
the approved authority even if a configured list is mutated after validation.
External CDN redirects remain misses; expanding the approved set requires a
reviewed code/policy-version change with primary-source evidence. Automatic decompression,
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

## Isolated product-image integration

Build only the worker-project Docker context; never use repository-root `out/crashes`
as product publish output or build context:

```bash
dotnet restore src/Server/Avalon.Crashes.Worker/Avalon.Crashes.Worker.csproj -r linux-x64
dotnet publish src/Server/Avalon.Crashes.Worker/Avalon.Crashes.Worker.csproj -c Release -r linux-x64 --self-contained false --no-restore -p:UseAppHost=false -o src/Server/Avalon.Crashes.Worker/out
docker build --platform linux/amd64 -t avalon-crashes-worker:integration src/Server/Avalon.Crashes.Worker
bash tools/crashes/run-integration.sh --suite postgres
bash tools/crashes/run-integration.sh --suite garage
# Set protected absolute paths first; neither directory enters the image.
export AVALON_CRASHES_PRIVATE_PAIR=/absolute/private/pair
export AVALON_CRASHES_PRIVATE_FIXTURE=/absolute/private/fixture
bash tools/crashes/run-integration.sh --suite native
bash tools/crashes/run-integration.sh --suite all
```

Requires Bash, Docker Compose v2/local Linux-amd64 daemon, Python3.12+ (`python`),
and .NET10 SDK. `CRASHES_IMAGE` selects an already-built product image. Windows
uses Git Bash, not WSL. The SDK runner image is digest-pinned to10.0.402 and tests
are published for runtime10.0.13 to agree with the product image. Tool versions
and native binaries still come exclusively from the canonical worker native lock.

Every invocation generates a UUID Compose project, volumes and scratch directory.
PostgreSQL listens on5433 only inside its container. Tests share that network
namespace, preserving the fixture's localhost/non5432 guard. No host ports are
published. Garage is available only through `https://garage-tls:8443` in the
internal test network; its disposable self-signed certificate is trusted only
through the test container's `SSL_CERT_FILE`. No host trust store is modified.
All database/storage credentials are generated in mode-private files and supplied
with env/config files. Supplied DB/S3 variables, Compose overrides, remote Docker
contexts and `DOCKER_HOST`/TLS overrides are rejected before provisioning.

Suite traits are `Category=CrashesPostgres`, `Category=CrashesGarage`, and
`Category=CrashesNative`. Protected `all` additionally executes `Category=CrashesVertical` with private mounts and the generated PostgreSQL/HTTPS Garage network. The native suite remains `--network none`, including under `all`. `all` checks each suite separately. Zero tests, any skip,
failed results or attachments fail the run. The native suite requires the exact
pair/dump hashes from committed provenance; missing protected prerequisites fail
explicitly. PostgreSQL and Garage suites run independently without private files.
The Garage smoke proves signed HTTPS paging, HEAD/GET and changed-fingerprint
rejection. S3 timestamps are floored to whole UTC seconds because Garage LIST
and HTTP HEAD/GET expose different precision; retention can expire less than one
second earlier, never later. ETag, size and whole-second changes remain checked.

Exit/interrupt cleanup removes only this generated project's containers, network,
volumes, credentials and private logs/results. No integration TRX/log/fixture
uploads are configured. Forced process termination or daemon loss can prevent
cleanup: inspect exact `crashes-test-<UUID>` names/labels before removing only that
run's resources; never prune the user's Docker host. The protected source files
remain untouched. The protected vertical suite runs the shipped `/app` worker executable, real pinned conversion/walking, restricted-role identity HTTP and a digest-pinned OTLP collector. Public Garage tests prove signed worker read-only original/crash access, cache writes, anonymous denial and SQL-role denials without private fixtures.

The `crashes-integration.yml` workflow is manual only (Actions → Crash integration → Run workflow, on any
branch): pull requests don't run it. Run it after changing the crash pipeline. The ordinary suites use ephemeral
GitHub-hosted runners. Full native plus vertical acceptance is manual, main-only, in the protected
`crashes-native` environment. Configure required reviewers and main-only deployment
branches there, plus `CRASHES_FIXTURE_URL` (secret signed HTTPS archive URL) and
`CRASHES_FIXTURE_SHA256` (trusted environment variable). The archive itself must
never be a GitHub secret/artifact: it exceeds normal secret limits. Provisioning
rejects redirects, validates SHA256/size and extracts only regular allowlisted
files matching public provenance into runner temp, after the product image build.
The tar.gz must contain file entries `pair/runtime.exe`, `pair/runtime.pdb`,
`fixture/dump.dmp`; optional entries are `fixture/runtime.sym`, `fixture/meta.json`,
`fixture/log-tail.txt` and `fixture/provenance.json`. Directory, duplicate and link
entries are rejected. Neither URL nor credentials are placed on command lines.

## Focused acceptance and manual operation

For a development iteration on vertical tests only, use
`CRASHES_FOCUSED_VERTICAL=1 bash tools/crashes/run-integration.sh --suite all`.
This still requires/provisions the protected fixture and isolated services, but
selects only `CrashesVertical`; it is not evidence that the other suites ran.
Unset that variable for the required full acceptance command. Ordinary solution
runs skip opt-in integration tests explicitly and never substitute for this gate.

A manual worker is opt-in and does not change Aspire. Supply an operator-owned,
mode-private environment file with `Database__Crashes__ConnectionString`,
`Crashes__Storage__Endpoint` (HTTPS), `Crashes__Storage__AccessKeyId` and
`Crashes__Storage__SecretAccessKey`. Choose a private Docker network that reaches
only the intended disposable services; this is separate from the harness, which
refuses supplied endpoints. Example after provisioning those local prerequisites:

```bash
docker run --rm --name avalon-crashes-local --network YOUR_PRIVATE_NETWORK \
  --read-only --cap-drop ALL --security-opt no-new-privileges --memory 1g --cpus 1 \
  --tmpfs /scratch:rw,nosuid,size=2g,uid=0,gid=1654,mode=2770 \
  --tmpfs /symbols:rw,nosuid,size=4g,uid=0,gid=1654,mode=2770 \
  --env-file /absolute/private/crashes.env \
  -e Crashes__Processing__ScratchRoot=/scratch/reports \
  -e Crashes__Symbols__CacheRoot=/symbols/cache \
  avalon-crashes-worker:integration run --paused
```

For private local TLS, mount only its CA certificate read-only and set
`SSL_CERT_FILE` in that container. Never disable certificate validation. Remove
`--paused` only after reviewing storage/DB grants and retention. Cleanup uses
`cleanup` and a separate environment file containing only the crash DB connection.
It has no storage credentials; deployment must also enforce its PG/DNS-only egress.

## Acceptance evidence matrix

| Gate | Reproducible evidence | Boundary |
| --- | --- | --- |
| Windows Release pair and conversion | `verify-native.sh --fixture ... --private-pair ...` | Private exact PE/PDB/dump; source/CFI/inlines and precise wrong-symbol rejection |
| PostgreSQL | `run-integration.sh --suite postgres` | Real migrations, locking, aggregate/idempotency, expiry and triage |
| HTTPS Garage and grants | `run-integration.sh --suite garage` | Signed paging/conditional reads; worker read/cache-write and denied writes; restricted SQL role |
| Product Linux native process | `run-integration.sh --suite native` | Offline pinned tools, process limits/reaping; UID1654, read-only root, dropped capabilities |
| Complete private pipeline | `run-integration.sh --suite all` | Additional four protected vertical scenarios; actual worker, Admin/PAT HTTP, restart, expiry, regression/conflict and collector failures |
| API contract | ordinary identity/API tests and generated OpenAPI | Primitive/nullable DTOs, errors, authorization and route/host boundaries |
| Packaging | both Helm `ci/test.sh`, `tools/release/test.sh`, release Python tests | Rendering, release/tag propagation and generated manifests; no deployment implied |
| External rollout | homelab#255 and Dashboard#431 | Dedicated DB/WAL/snapshot exclusions, crash lifecycle, original backups, CNI, alerts, protected CI environment and generated Dashboard client remain external gates |

Only finite test counts/statuses are public evidence. Raw dumps, original symbols,
stackwalk JSON, collector captures, credentials and TRX are private and never
uploaded. The operational contract is [docs/crash-analysis.md](../../docs/crash-analysis.md).

Verified locally on 2026-10-24 against the rebuilt Linux worker image: full harness
PostgreSQL 68/68, Garage 3/3, offline native 12/12 and protected vertical 4/4, with
zero integration skips. The collector received 4 process spans and 4 finite events
correlated across the success/fetch/tool/DB-error reports, plus 15 worker metric
names; private-value scans passed. The ordinary Release solution had 9,898 passes,
0 failures and 108 skips (85 opt-in crash cases plus 23 existing skips). The separate
exact private conversion/source/CFI/inline/wrong-symbol command also passed.
These are disposable local results, not deployment or remote-workflow evidence.
