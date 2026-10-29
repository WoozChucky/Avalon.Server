# Private crash analysis

The optional `Avalon.Crashes.Worker` processes existing Windows x64 Release crash
ZIPs from Garage into a separate PostgreSQL database. Identity exposes bounded
stack/log views and group triage to account-backed Admin bearer sessions. The game
servers, login availability and normal Aspire startup do not depend on this service.
There is no raw dump, ZIP, executable, PDB or signed-download API.

## Components and trust boundaries

| Component | Responsibility | Credentials |
| --- | --- | --- |
| `Avalon.Crashes` | Bounded parsing, stack projection, signatures and contracts | None |
| `Avalon.Database.Crashes` | Migrations, durable queue, atomic counts, reads and triage | Separate crash PostgreSQL |
| `Avalon.Crashes.Worker run` | Migrate, purge, discover, convert and process | Crash DB owner; scoped Garage worker key |
| `Avalon.Crashes.Worker cleanup` | Purge expired report-bearing rows | Crash DB only; no Garage key |
| Identity `/admin/crashes` | Read and triage through the generated REST contract | Restricted crash DB role; no crash S3 key |

Worker storage grants are read on `crashes` and `symbols`, and read/write on
`symbols-sym`. No anonymous access is permitted. Original symbols remain private
and immutable to the worker. Converted cache is disposable. Object keys inside
`crashes` are `<version>/<yyyy-MM-dd>/<guid-N>.zip`, without a second `crashes/`
prefix. Originals use `runtime.exe/<image-key>/runtime.exe` and
`runtime.pdb/<debug-id>/runtime.pdb` from the dump's exact PE/PDB identity.

The worker is the only migration owner. Provision a dedicated database owned by
its login, then let `run --paused` apply the generated EF migrations. Identity's
role needs CONNECT to that database and these grants (replace the role name with
the provisioned login; do not give it ownership or membership in the worker role):

```sql
GRANT USAGE ON SCHEMA public TO crashes_identity;
GRANT SELECT ON crash_reports, crash_groups, "__EFMigrationsHistory" TO crashes_identity;
GRANT UPDATE (status, fixed_in_version, issue_url, notes, is_regression,
              triage_revision, triaged_at, fixed_at)
    ON crash_groups TO crashes_identity;
```

Identity must not INSERT/DELETE reports, update lifetime aggregates, create schema
objects or alter migration history. Readiness reads the initial migration history;
it never runs a migration. Errors are cached for 30 seconds and bounded to five
seconds per probe. Crash DB failure produces feature-local 503s after authorization;
it is not an identity startup or global health dependency.

## Input and execution limits

Only `dump.dmp`, `meta.json` and optional `log-tail.txt` are accepted. ZIP download
and total expanded input are each at most 4 MiB; metadata is 4 KiB and log tail
64 KiB. `meta.json` is schema 1 or 2. Schema 2 (Client#459, #1252) adds what the client
knew: `gpuVendorId` and `gpuDeviceId` (u32), `gpuName` (at most 128 printable characters),
`driverVersion` (at most 32 of `[0-9A-Za-z.+-]`, stored as `VulkanDriverVersion`, apart from
the dump-derived `GpuDriverVersion`) and `lastState` (`boot`, `menu`, `loading` or
`in-world`). Each is optional: a malformed one is dropped, never a reason to refuse the
report. `gpu_name` and `vulkan_driver_version` are columns, so a group's detail breaks
its reports down `ByGpu` beside `ByGpuDriver`; reports without them read `unknown`.
Unsafe ZIP names, links, duplicate entries, CRC/range errors and malformed
metadata/dumps are rejected before tools.

| Bound | First-release value |
| --- | --- |
| Poll / backfill reservation | 60 seconds / at least one quarter of discovery work |
| Upload notices (#1252) | `POST /internal/crash/notice` `{key}` on 8080, from `networkPolicy.noticePeers` only: the worker HEADs the object (the proof of the upload, which the API cannot see) and queues it at once; 64 waiting, extras left to the poll |
| Concurrent reports / converters | 1 / 1 |
| Report lease / heartbeat / attempt | 10 minutes / 30 seconds / 8 minutes |
| Converter wall / CPU | 120 / 90 seconds |
| Stackwalk wall / CPU | 60 / 45 seconds |
| Tool/resource failures before failed | 3 |
| Transient retries | 1m, 5m, 15m, 1h, then at most hourly; stop after 24h or expiry |
| Optional public symbols | 60 seconds, at most 16 modules |
| HTTP operation / artifact / cumulative download | 30 seconds / 256 MiB / 1 GiB per attempt |
| Converter output / stackwalk stdout / stderr | 256 MiB / 16 MiB / 64 KiB |
| Stored stack | 2 MiB; 128 threads, 256 physical frames/thread, 16 inline entries/frame, 512 modules |
| Scratch / local symbols | 2 GiB / 4 GiB, disjoint disposable roots |
| Worker request / limit | 100m CPU, 256 MiB RAM / 1 CPU, 1 GiB RAM |

Crashing-thread frames are preserved first; truncation and missing symbols are
explicit quality flags. Paths reduce to safe basenames. Group signatures are
deterministic and insensitive to load addresses; processed reports never regroup
automatically. Missing optional system symbols do not invalidate an otherwise
usable runtime stack. Public policy v1 permits only the fixed Microsoft symbol
origin; dump contents cannot choose hosts, subprocess arguments or credentials.

Linux amd64 tooling is pinned to Mozilla `dump_syms` 2.3.9 and rust-minidump
`minidump-stackwalk` 0.27.0, including binary digests. The authoritative release
URLs, revisions, hashes and licenses are in
[`native/tools.lock.json`](../src/Server/Avalon.Crashes.Worker/native/tools.lock.json).
Conversion uses the exact adjacent executable/PDB, inline data and x64 CFI; the
downloaded executable is never executed. Native processes have scrubbed
environments, process-group termination and resource bounds. The pod runs UID1654,
read-only, with dropped capabilities, runtime-default seccomp and no API token.

## Retention, counts and privacy

Report expiry is exactly 90 days from Garage's first observed upload timestamp.
LIST/HEAD/GET timestamps are canonicalized to whole UTC seconds, conservatively
expiring less than one second earlier. Client times, retries, fixes and processing
never extend that deadline. Expiry is enforced by reads and completion as well as
startup/nightly cleanup. Cleanup removes report metadata, stacks, logs and queue
rows; lifetime group history remains. Scratch is removed after attempts and stale
scratch is swept at startup. Cache contains symbols, not crash dumps.

`reportCount` is lifetime count. `retainedReportCount` and `matchingReportCount`
refer to declared retained/filter windows; the 30-day series is zero-filled.
Expired groups remain readable with null latest report/stack. An absent or expired
report is the same 404. Group notes are long-lived engineering notes: never copy
logs, names, paths, account information or any other per-report material into them.
Notes do not expire with a report.

homelab#255 must supply a **dedicated, non-backed-up crash PostgreSQL instance**,
including an explicit policy excluding its WAL archives, snapshots, replicas and
volume snapshots from report-bearing retention beyond 90 days. A database in the
shared backed-up auth cluster is insufficient. A shorter equivalent upper bound
needs separate evidence before enabling it. Configure crash-bucket lifecycle and
verify deletion timing independently. Preserve the existing backup of original
`symbols`; converted cache may be rebuilt. These infrastructure gates are not
proved by chart rendering or local tests.

## API and Dashboard#431 handoff

Identity owns `GET /admin/crashes/groups`, `GET /admin/crashes/groups/{id}`,
`PATCH /admin/crashes/groups/{id}` and `GET /admin/crashes/reports/{id}`. Enable
`Application:Crashes:Enabled` and supply identity's restricted
`Database:Crashes:ConnectionString`. Feature defaults off. Use explicit Admin
bearer sessions; PATs, cookies alone and Console-only accounts are refused. Every
crash response has `Cache-Control: no-store`, including errors.

Dashboard#431 should regenerate its client from the existing published OpenAPI v1
document, using the named `Avalon.Api.Contract.Crashes` DTOs. UUIDs are primitive
strings, dates UTC ISO, addresses hex strings and enum values lower case. Unknown
native/internal fields remain nullable, with explicit quality markers; do not
invent an empty stack or zero address. The feature preserves explicit JSON nulls.
Decoded NUL characters in retained log/native text become U+FFFD before signatures
and persistence, with the additive `sanitizedText` quality marker. UTF-8 log and
serialized stack limits still apply after replacement; `truncated` also marks any
resulting omission. Clients with strict quality enums must regenerate for this
marker. Deploy matching feature worker and identity binaries before first intake;
intermediate pre-fix branch images are not supported rollout targets and cannot
decode newly flagged rows. Leave the additive quality constraint migration in place during rollback and use compatible binaries.

PATCH requires `triageRevision` and at least one editable field. Omitted fields
preserve values; explicit null clears `fixedInVersion`, `issueUrl` or `notes`.
Status accepts `new`, `investigating`, `fixed`, `ignored`; fixed requires strict
SemVer 2.0. Fixing records server `fixedAt`. A later upload at/above that fixed
version reopens a currently fixed group and increments regression history once per
fixed-to-new reopening; later reports while already reopened do not increment it.
A delayed old upload cannot regress it merely by arriving at the processor later. Fix and ignored clear the current
regression flag while preserving regression history. Conflicts are 409 with only
`currentRevision`, requiring a reread before editing. Issue URLs are validated
GitHub issue URLs and are never fetched. Notes containing NUL are invalid input (400).

| HTTP | Feature code |
| --- | --- |
| 400 | `CRASH_INVALID_INPUT` |
| 404 | `CRASH_GROUP_NOT_FOUND`, `CRASH_REPORT_NOT_AVAILABLE` |
| 409 | `CRASH_GROUP_CHANGED` |
| 503 | `CRASH_ANALYSIS_DISABLED`, `CRASH_ANALYSIS_UNAVAILABLE` |

Report 404 title is `This report has expired or is unavailable.` Authorization
precedes availability checks. Lists omit logs/notes; group detail contains only
the latest retained crashing-thread view; full typed bounded analysis and optional
log tail appear only on report detail. Dashboard implementation/readiness is a
separate gate; this server branch does not implement it.

## Startup, operations and rollback

Build/run instructions and protected acceptance are in
[`tools/crashes/README.md`](../tools/crashes/README.md). Manual container startup
is opt-in. Normal `dotnet run --project src/Server/Avalon` does not acquire private
crash storage or start this worker.

1. Provision isolated PostgreSQL, Garage grants, lifecycle and original-symbol
   backup; configure the Secrets and chart network peers. Audit the union of all
   selecting NetworkPolicies and test actual CNI enforcement.
2. Install one worker replica with `run --paused`. It migrates, validates pinned
   tools and purges expired backlog before intake. Verify `/alive` and `/health`
   on the private worker Service. Identity's feature flag can remain off.
3. Install the DB-only cleanup CronJob (nightly UTC), verify least privileges,
   telemetry and current expired-row count, then enable worker intake. Discovery
   is paged/resumable; recent prefixes and reserved historical work prevent a
   large old-build backlog from starving current arrivals. Overlapping stale
   discovery saves may repeat scans; deduplicated durable object keys prevent
   lost candidates or duplicate contributions. Keep the first release at one
   worker; multi-worker scheduling is not an approved scaling mode.
4. Enable identity's flag with its separate SQL login and validate an Admin
   session/PAT denial. Enable Dashboard separately after its generated-client work.

Worker arguments are `run`, `run --paused`, `cleanup`,
`list-failures --page 1 --page-size 50`, and `retry --report-id <uuid>`. Operator
commands accept IDs and finite failure state, never raw stack input. Manual retry
applies only to eligible retained failed reports, starts a bounded new retry
campaign governed by the retry backoff; it cannot extend expiry or regroup a
processed row. The operator command has no separate elapsed-time rate limit.
Correct missing originals/configuration first. Do not reset counters or delete
queue rows to replay reports.

For rollback, disable the identity crash feature and pause worker intake first.
Keep cleanup running. Pin the last verified compatible image/chart; do not roll
back schemas blindly or restore report-bearing backups. Drain/terminate active
attempts using the pod's 60-second grace; an unfinished lease is reclaimable after
its bounded deadline. Old reports can expire while paused and will be purged before
resume. Never turn off expiry to clear a backlog.

Worker telemetry exports only finite process spans, sanitized worker events and
`avalon.crashes.*` metrics. Monitor queue depth/oldest age, inventory sweep age,
retries/tool failures, symbol cache outcomes, scratch/cache bytes, expired rows,
retention cutoff and cleanup's last success. Version series are bounded. Do not
add object keys, signatures, filenames, URLs, SQL, logs or arbitrary labels. The
optional OTLP collector needs deliberate egress peers; there is no invented public
metrics endpoint. Alerts and dashboard provisioning remain homelab#255 work.

Local acceptance proves disposable services, native Linux execution and API
contracts. It does not prove live deployment, production performance tuning,
remote protected-workflow configuration, lifecycle deletion, backup exclusions,
CNI enforcement or Dashboard readiness. Keep those gates explicit before rollout.
