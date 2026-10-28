# Tooling

`tools/` holds the offline utilities. They are not part of the server's runtime and nothing in
`src/Server` depends on them.

| project | what it does |
| --- | --- |
| `Avalon.Balance` | the balance simulator's command line ([balance](balance.md)) |
| `Avalon.Benchmarking` | BenchmarkDotNet harnesses, the `crowd-budget` and `outbox-flush` steady-state harnesses, and the `navmesh-bake` and `candidate-ratio` (#982: a spatial index's view candidates along generated forests) measurements ([benchmarks](benchmarks.md)) |
| `Avalon.ChunkGen` | generates the forest pieces and the town squares into the chunk catalog, the decor the client draws on them, and both maps' scenery rings (`Maps/Scenery`: each piece's drawn terrain and the ring files, the pieces' decor in `Maps/Decor`); `kit-bounds` measures the art kits ([map generation](map-generation.md)) |
| `Avalon.LoadTest` | load testing ([load testing](load-testing.md)): `provision` creates a run of bot accounts as an admin and keeps it in a local run file; `check` takes one bot (or one fighters' party) end to end into the run's world; `ramp` adds headless bots in steps (idle, walking, changing character, quitting the world and joining again, or fighting through the forest, alone or in parties), judges each step against the limits (Prometheus, the bots, the bot PC) and writes a report with the capacity and what failed first; `cleanup` deletes the run |
| `Avalon.LocalDev` | local runs ([development setup](development-setup.md#from-clone-to-client-in-world)): `setup` makes the world's three local TLS certificates and writes the API's and the world server's user-secrets for them; `login` signs in over the REST API and hands the game client its game ticket; `check` walks the client's REST chain up to a join ticket, waiting in a full world's line when the join answers 202 (#967: it checks in as told, refreshes the game context as it comes due, and asks again at its turn; Ctrl+C leaves the line) |
| `Avalon.Scenarios` | the scenario runner (below): runs the world server's fixed scenarios in process and reports tick-thread allocations, tick times and GC |
| `Avalon.TraceReader` | the trace reader (below): reads a world's captured traces into CSV and TSV files: `tick` (the tick provider's records), `kcpu` (a `collect-linux` trace's kernel CPU samples and stacks), `survey` (what a trace holds) |
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
| `item-schema` | `schema/items/item-schema-v1.json` | `ItemTemplate` and its ten enumerations (`ItemBaseKind` and `ItemBaseStat` since #1179), with `baseStats`, a list of objects whose own fields (`stat`, `min`, `max`) are listed under it, and `baseTemplateId` (#1179, part B2) |
| `item-catalog` | `schema/items/item-catalog-v1.json` | the item template rows, and since #1179 (part B2) each template's `baseStats` (`stat` an `ItemBaseStat` number, `min`, `max`, by stat: the ranges every copy rolls within, a weapon's damage and its attack time in milliseconds before attack speed among them; empty for an item with none), a weapon's `damageType`, and a unique's `baseTemplateId` (the base whose base stats and implicit its copies roll), each of the last two left out when null; the template's damage columns and `attackTimeMs` are gone. Since #1178 the stat types 19-34 (the thirteen increased damage and healing percentages, crit multiplier, cast speed and cooldown recovery; 8 and 9, the old damage stats, retired), and since #1179 each base's `baseKind` and `requiresAnyAttribute`, a template's `rarity` in the new numbering (`Common` 0 or `Unique` 3; a copy's rarity, item level, base stats and modifiers travel with the copy, never in the catalog) and no `itemPower` (the item stats 35-53 are rolled on a copy's modifiers, not a template's; 53 is increased maximum life, part B2) — **needs a World database** |
| `item-modifier-catalog` | `schema/items/item-modifier-catalog-v1.json` | the item modifier catalog (#1179) from `ItemModifierSeed` and `ResonanceSeed`, no database: the Resonance themes (#1180: `themes`, `id` and `name`, and since part A2 each theme's `effects`, by id: `count`, `trigger` 1 hit to 6 ward broken, `chancePct`, `cooldownMs`, `auraId` in aura-catalog, `target` 1 wearer, 2 hit target, 3 attacker, `chainTargets`, `atOrBelowLifePct` and `damageType`, the last two null when unset), the groups (`id`, `kind` 1 prefix, 2 suffix, 3 unique, 4 implicit, `code`, `element`, `name`, `themeId`, set on a prefix or suffix group and null on a unique or implicit one), the modifiers (`id`, `groupId`, `tier`, 1-9 counting down on a prefix, suffix or implicit and null on a unique, stat lines with `min`-`max` and an "adds" line's `min2`-`max2`) and each template's linked modifier ids (a base's one implicit at its tier, a unique's own modifier; a unique's copy rolls its base's implicit first, `baseTemplateId` in item-catalog), so a client tells implicits and a unique's own modifier from rolled affixes; since #1179 part B2 the implicits are 26 groups shared by stat (an implicit group has no `code`); `ItemModifierDocShould` fails when the committed file differs from the seed's render |
| `item-modifier-doc` | the generated block of the repository's `docs/item-modifiers.md`, whatever `--out` names (the page is hand-written and committed, not an artifact) | the same seed as the catalog's tables (the themes and their families, each theme's effects (#1180 part A2: count, trigger, chance, cooldown, the aura applied, on whom, chain and life threshold), each family with its theme, the tiers, the implicits, the bases and the uniques), between the page's markers; its readiness refuses, before anything is written, a missing page or markers missing, doubled or out of order; `ItemModifierDocShould` fails when the committed block differs |
| `ability-catalog` | `schema/abilities/ability-catalog-v1.json` | every ability row a client names and draws, creatures' included (#163), with its `auraId`, `basic` (a class's basic attack, which a silence does not stop, #770) and `damageType` (#1178: 0 physical, 1 fire, 2 cold, 3 lightning, 4 blight, `null` for none, which a creature's own ability leaves to its creature's base type) and `tags` (#1178, flags: 1 attack, 2 spell, 4 heal, 8 area, 16 projectile, 32 melee); `castTimeMs` is before cast speed and `cooldownMs` before cooldown recovery, and a basic attack's pace is its weapon's (the copy's rolled attack time, within the `AttackTime` base-stat range in item-catalog's `baseStats`) — **needs a World database** |
| `aura-catalog` | `schema/auras/aura-catalog-v1.json` | every aura's name, icon, kind, timing, stacking, crowd control (`control` flags) and break chances, and stat modifiers (since #1178 `stat` 5 is evasion, 11-15 the four resistances and max ward, 16-28 the increased damage and healing percentages, 29 the crit multiplier and 30-32 attack speed, cast speed and cooldown recovery, 2, 3 and 7 retired; `kind` 1 flat, 2 increased, 3 more); what a periodic aura deals stays on the server — **needs a World database** |
| `doodad-catalog` | `schema/doodads/doodad-catalog-v1.json` | every doodad's model kit and key, clips, interaction time and range, solidity, spent look and `service` (the window a click opens, `1` workbench, `2` lectern, or null; #1179, #1181), nothing of what a use does (no gates, loot, gold, restore, radius or aura outcomes) — **needs a World database** |
| `map-catalog` | `schema/maps/map-catalog-v1.json` | every creature display id each map can show (placed creatures plus its spawn table), for client preloading — **needs a World database** |
| `quest-catalog` | `schema/quests/quest-catalog-v1.json` | every quest's enUS title, stages, objectives and rewards, for tooling (#714) — **needs a World database** |
| `talent-catalog` | `schema/talents/talent-catalog-v1.json` | the shared talent tree (#1181) from `TalentSeed`, no database: the nodes by id (`id`, `kind` 1 start to 7 junction, `name`, `angle` in degrees, `radius` in layout units, `forkGroupId`, `abilityId`, `keystone` (the rule a keystone bends, or null), `stats` by stat then kind: `stat` as StatType, `kind` 1 added or 2 more, `value`, `valueMax` on an "adds" line, and `modifiers` by kind on a capstone: `kind` 1-8, `abilityId`, `value`, `value2` on an explosion), the edges once each (`a` < `b`), the nine fork groups and the class starts (`class`, `nodeId`); [talents](talents.md); `TalentCatalogExportShould` fails when the committed file differs from the seed's render |

`item-catalog`, `ability-catalog`, `aura-catalog`, `doodad-catalog` and `quest-catalog` read their World connection string only from the environment
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
start (see [map-generation.md](map-generation.md)). Its `forest` and `town` commands also write each map's scenery
ring into `Maps/Scenery/` (the pieces' terrain and the ring file the World server reads at start-up) and the pieces'
decor into `Maps/Decor/`. `Avalon.ChunkGen` stays a tool of its own because
it writes the server's source data, the chunk catalog and the ring files the World server reads, not an artifact
derived for clients; how to run it is under "Generated forest chunks" and "Scenery ring" in map-generation.md.

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

`tools/Avalon.Scenarios` runs the world server's fixed scenarios (`town-idle`, `town-walk`, `town-social`,
`many-instances`, `forest-combat`, `forest-resonance`, `forest-awakening`, `town-doodads`, from `tests/Avalon.World.Testing`) in process, with the server's GC settings, and prints one row per
scenario: the
tick-thread bytes of one 60-tick window (the least of five, after the scenario's own fixed number of warm-up ticks,
as the allocation gate measures them; for `forest-combat`, `forest-resonance` and `forest-awakening`, fights of fixed length, the mean over the measured
minute), bytes per player per tick, tick times (mean, p50, p95, p99, max), the share of ticks over the
60 Hz budget, and the timed phase's collections and GC pauses. Run it in Release;
`--write-baseline perf/local/<host>.json` keeps a run (`perf/local/` is ignored by git), `--baseline <file>` prints
current, baseline and change per figure against such a run and never fails, and `--write-allocations <file>` writes
the committed allocation baseline from this run. `--broadcast-ticks <n>` adds, after the timed runs, a census of how
many instances broadcast state on each of `n` untimed ticks, read from the world's own tick trace: it shows broadcast
lockstep ([benchmarks](benchmarks.md#comparing-broadcast-schedules-976)). Timings are machine-specific and only compare
on one machine.

```bash
dotnet run -c Release --project tools/Avalon.Scenarios -- --scenario all --write-baseline perf/local/$(hostname).json
dotnet run -c Release --project tools/Avalon.Scenarios -- --baseline perf/local/$(hostname).json
```

## The trace reader

`tools/Avalon.TraceReader` reads the `.nettrace` files captured from a world ([tick phase traces](tick-profiling.md#the-trace-reader),
where its modes and output columns are described): `survey <trace>` lists what a trace holds, `tick <trace> <out-dir>`
writes the `Avalon-World-Tick` records to `instance.csv`, `world.csv` and `batch.csv`, and `kcpu <trace> <out-dir>`
resolves a `collect-linux` trace's kernel CPU samples through TraceEvent's `TraceLog` into `ksamples.tsv` and
`kstacks.tsv`. It uses `Microsoft.Diagnostics.Tracing.TraceEvent` 3.2.8. The analysis of those files (scripts) stays
outside the repository.

```bash
dotnet run -c Release --project tools/Avalon.TraceReader -- tick tick.nettrace out/tick
```
