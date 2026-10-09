# Tooling

`tools/` holds the offline utilities. They are not part of the server's runtime and nothing in
`src/Server` depends on them.

| project | what it does |
| --- | --- |
| `Avalon.Balance` | the balance simulator's command line ([balance](balance.md)) |
| `Avalon.Benchmarking` | BenchmarkDotNet harnesses, and the `crowd-budget` and `outbox-flush` steady-state harnesses ([benchmarks](benchmarks.md)) |
| `Avalon.ChunkGen` | generates the forest pieces and the town squares into the chunk catalog ([map generation](map-generation.md)) |
| `Avalon.LoadTest` | load testing ([load testing](load-testing.md)): `provision` creates a run of bot accounts as an admin and keeps it in a local run file; `check` takes one bot (or one fighters' party) end to end into the run's world; `ramp` adds headless bots in steps (idle, walking, changing character, or fighting through the forest, alone or in parties), judges each step against the limits (Prometheus, the bots, the bot PC) and writes a report with the capacity and what failed first; `cleanup` deletes the run |
| `Avalon.LocalDev` | local runs ([development setup](development-setup.md#from-clone-to-client-in-world)): `setup` makes the world's three local TLS certificates and writes the API's and the world server's user-secrets for them; `login` signs in over the REST API and hands the game client its game ticket; `check` walks the client's REST chain up to a join ticket |
| `Avalon.Scenarios` | the scenario runner (below): runs the world server's fixed scenarios in process and reports tick-thread allocations, tick times and GC |
| `Avalon.Exporter`, `Avalon.Exporter.Emitters` | exports every artifact the client vendors, one subcommand per artifact |
| `api-smoke` | `smoke.sh`, a read-only smoke check of the deployed API (below) |
| `release` | `channel-version.sh` (dev, nightly and release version numbers) and `registry_cleanup.py` (prunes old dev and nightly images from the container registry), with their tests; run by the CI, nightly, release and registry-cleanup workflows |

## The exporter

Three projects used to sit here because a client needed a specific artifact exported from the
server's own types, and each arrived as its own `csproj` with its own bootstrap and its own output
convention. That did not scale: the next vendored artifact would have added a fourth by the same
reasoning. The schema generator and the chunk rotation vectors tool are now one project, and adding an
export is an entry in `Exports.s_all` (`tools/Avalon.Exporter/Export.cs`) rather than a new project.

```bash
dotnet run --project tools/Avalon.Exporter                    # lists the artifacts, writes nothing
dotnet run --project tools/Avalon.Exporter -- all             # writes every artifact
dotnet run --project tools/Avalon.Exporter -- proto corpus    # writes just those
dotnet run --project tools/Avalon.Exporter -- all --out /tmp  # somewhere other than schema/
```

| name | writes | from |
| --- | --- | --- |
| `proto` | `schema/avalon.proto` | the `[ProtoContract]` packet types |
| `opcodes` | `schema/opcodes.json` | the opcode and encryption-flag reflection |
| `corpus` | `schema/corpus/*.txt` | the server's serializer, one file per message |
| `crypto` | `schema/crypto/session-v1.txt` | the production `AvalonCryptoSession` and `SessionKeys` |
| `rotation` | `schema/vectors/rotation-v1.txt` | `ChunkRotation.LocalToWorld` via the real layout generator |
| `object-guid` | `schema/vectors/object-guid-v1.txt` | `ObjectGuid`'s own shifts and masks |
| `navmesh` | `schema/vectors/navmesh-v1.txt` | the DotRecast bake and movement queries from the real generator |
| `item-schema` | `schema/items/item-schema-v1.json` | `ItemTemplate` and its eight enumerations |
| `item-catalog` | `schema/items/item-catalog-v1.json` | the item template rows — **needs a World database** |
| `ability-catalog` | `schema/abilities/ability-catalog-v1.json` | every ability row a client names and draws, creatures' included (#163) — **needs a World database** |
| `aura-catalog` | `schema/auras/aura-catalog-v1.json` | every aura's name, icon, kind, timing, stacking and stat modifiers — **needs a World database** |
| `quest-catalog` | `schema/quests/quest-catalog-v1.json` | every quest's enUS title, stages, objectives and rewards, for tooling (#714) — **needs a World database** |

`item-catalog`, `ability-catalog`, `aura-catalog` and `quest-catalog` read their World connection string only from the environment
(`Database__World__ConnectionString`) or from user-secrets for `src/Server/Avalon.Database.World`,
the sources the `dotnet ef` design-time factories use (#557). None reads an `appsettings` file,
so running one from a folder that holds one cannot point it at that file's database. Without a
string, a call that names any of them (`all` included) stops before anything is written and says a World
database connection is needed.

Two rules the tool keeps, because both failures are silent ones:

- **Every name is resolved before anything is written.** A typo cannot export all but one of the artifacts
  and report the failure afterwards, leaving a tree nobody asked for.
- **Everything is written with explicit LF.** These files are hashed as bytes at the other end, so
  a CRLF is not a formatting nit — it is a hash the client cannot reproduce. `.gitattributes` holds
  the same line from the other side.

The exporter is two projects: `Avalon.Exporter.Emitters` holds everything derived from the code
alone and is what the drift tests reference, and `Avalon.Exporter` adds the CLI and the exports
that read a database. The split keeps EF Core and Npgsql out of the shared unit tests, which
reference the emitters only.

Nothing is exported from a transcription. Each artifact runs the server's own type, because a
re-typed constant agrees with whatever it was typed from — which is the failure these files exist
to catch.

## What is not in it

`Avalon.Benchmarking` is a harness that owns its own `Main`; it does not belong behind an export
subcommand. Chunk data is not exported either: `tools/Avalon.ChunkGen` generates the forest pieces and
the town's squares straight into `src/Server/Avalon.Server.World/Maps/`, beside the chunk files and the town
layout kept there as they are, and the World server seeds the database from there on
start (see [map-generation.md](map-generation.md)). `Avalon.ChunkGen` stays a tool of its own because
it writes the server's source data, the chunk catalog the World server reads, not an artifact derived
for clients; how to run it is under "Generated forest chunks" in map-generation.md.

## The API smoke check

`tools/api-smoke/smoke.sh` (#794) sends each line of `requests.tsv`, beside it, as a GET and compares the status with
the line's expected ones, to show after a deploy or a route move that each route group reaches a service that answers
it. Every rule of the route manifest has a line (`SmokeCoverageShould`).

```bash
BASE=https://avalon.example/api tools/api-smoke/smoke.sh
BASE=http://127.0.0.1:18080 AVALON_SMOKE_PAT=avp_... AVALON_SMOKE_GROUPS=worlds,distribution tools/api-smoke/smoke.sh
```

- `BASE` is the API's base URL, through the ingress or a `kubectl port-forward` of the release's service.
- `AVALON_SMOKE_PAT`, an Admin personal access token, is needed for the lines marked `pat`, which are skipped without
  it; it reaches curl on its standard input, never its command line.
- `AVALON_SMOKE_GROUPS` picks API services (`identity`, `worlds`, `commerce`, `distribution`); every group when unset.

It follows no redirect and prints each request's group, method, path and status, never a response body or the token.
It exits 0 when every status is one its line expects, 1 when one is not (000: no answer), and 2 on bad input.

## The scenario runner

`tools/Avalon.Scenarios` runs the world server's fixed scenarios (`town-idle`, `town-walk`, `many-instances`, from
`tests/Avalon.World.Testing`) in process, with the server's GC settings, and prints one row per scenario: the
tick-thread bytes of one 60-tick window (the least of five), bytes per player per tick, tick times (mean, p95, p99,
max), the share of ticks over the 60 Hz budget, and the timed phase's collections and GC pauses. Run it in Release;
`--write-baseline perf/local/<host>.json` keeps a run (`perf/local/` is ignored by git), `--baseline <file>` prints
current, baseline and change per figure against such a run and never fails, and `--write-allocations <file>` writes
the committed allocation baseline from this run. Timings are machine-specific and only compare on one machine.

```bash
dotnet run -c Release --project tools/Avalon.Scenarios -- --scenario all --write-baseline perf/local/$(hostname).json
dotnet run -c Release --project tools/Avalon.Scenarios -- --baseline perf/local/$(hostname).json
```
