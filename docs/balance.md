# Balance simulator and Balance Service

The balance simulator library and tool, and the in-cluster service that runs it for the workbench. File formats are in `balance/README.md`.

## Balance Simulator

The simulator is a library with a thin tool on top. `Avalon.Balance.Core` (`src/Server/`) is the simulator, grader and
run contract; it references `Avalon.Combat` and `Avalon.Domain` only, no `Avalon.World`, no `Avalon.Database*`, no EF,
pinned by `BalanceCoreAssemblyShould`. `Avalon.Balance.Data` reads the seed and the files (`SeedSource`, `OverrideFiles`,
`ConfigFileStore`) and references Core and `Avalon.Database.World`. `tools/Avalon.Balance` references Core and Data
only: it parses options, loads, calls `Simulation.Run` and writes `balance/out/report.html` and `results.csv`
(gitignored). It simulates one player against packs of forest creatures and grades win rate, fight length, health left,
class parity, level curve, resource flow and levelling pace against `balance/targets.json`. Run
`dotnet run -c Release --project tools/Avalon.Balance` (`--class`, `--scenario`, `--runs`, `--seed`, `--overrides`,
`--out`); exit 0 no red, 1 red, 2 refused input.
`SeedSource.Load` reads the seed from `WorldDbContext`'s design-time model (`IDesignTimeModel`, `GetSeedData()`, Npgsql
with a placeholder string, never connected) into `SeedTables`; `balance/overrides.json` (`"Table.key.Column": value`)
is applied to a copy of it. It never copies a balance formula: stats, hits, Fury, haste, costs, power regen, power
gains and heal caps (`PowerRegen`, `PowerPool`, `HealRules`, see World Simulation) are `Avalon.Combat`, the code the
world server runs, and so are the checks that validate the data: `AbilityRules`, `CombatDataRules` and
`CreatureTemplateRules` (and `ClassPowerType`, `Fury.DefaultFromDamageTaken`) in `Avalon.Combat`, which the world's
`AbilityCatalog`, `CombatPatch.Build` and `CreaturesPatch.Validate` delegate to with their exact checks and messages.

The run contract: `Simulation.Run(seed, defaults, request, progress, ct)` returns a `RunResult` (`Status` Done,
Invalid or Cancelled; rows, grades, `CheckedRows`, summary, applied overrides, `Seed`, `RunsPerRow`). A `RunRequest`
carries optional overrides (a `JsonElement`), config, a `RunFilter` (classes, levels, gear, scenarios), runs per row
and seed. A refusal is data, never an exception: `Issue(Path, Message)`, the path naming where (`overrides.<Key>`,
`seed`, `scenarios`, `targets`, `rotations.<Class>`, `filter.levels`). The seed is never changed, so one loaded
`SeedTables` serves many runs. `IProgress<RunProgress>` reports one row at a time from worker threads, best-effort; a
cancelled token returns Cancelled with no rows. A filter only drops rows and never changes the seed of the rest, so the
same data, config and request always give the same rows. `Catalog.Describe(seed)` lists every tunable
(key, table, column, type, seed value) for an editor, from the same table map the overrides use.

Creature kits (basic, specials in rotation order, ranged-only specials) are a static table, `CreatureKits.ByScript`,
keyed by script type name; the world test `CreatureKitParityShould` keeps it equal to the scripts in `Avalon.World`.
The small cast-system bookkeeping the simulator mirrors (the cooldown set when a cast fires, a creature's swing
interval, a creature's level range) and the fight's event order are pinned by `SimulatorParityShould` against
`CombatService`, the cast system and `CharacterEntity.Update`. It steps at the server's tick (1/60 s) so cooldowns,
casts and the event order count as the server's do. Mana and Energy regenerate `stat x coefficient x dt` a tick with
the fraction of a point carried between ticks (`PowerRegen.Amount`, the caller owning the carry), dropped while a cast
suppresses regen or the pool cannot regenerate (full included).
`ConfigFiles.Save` writes `scenarios.json`, `targets.json` and `rotations.json` in one canonical JSON form
(`ConfigFileStore.Write`), and the checked-in files are in that form, so a parse and save round-trips byte for byte.
Balance tables change only through `HasData` plus a migration (see Commands); `ModelDriftShould` fails when `HasData`
changed without one. `balance/README.md` has the file formats and what is not modelled (everyone in melee, no
projectile travel time, continuous combat, wind-ups always land).

## Balance Service

`Avalon.Balance.Service` (`src/Server/`) is an in-cluster ASP.NET service that runs the simulator for the workbench.
It is never exposed outside the cluster: a ClusterIP Service, no ingress. The REST API's worlds service
(`Avalon.Api.Worlds/Balance`, [API services](api-services.md)) proxies the admin `/balance/*` calls to it (`catalog`,
`runs` POST / GET `{id}` / DELETE `{id}`, `exports`), each behind `[Authorize(Policy = AvalonRoles.Admin)]`.

- **Endpoints:** `/catalog`, `/runs` (POST, GET `{id}`, DELETE `{id}`), `/exports`, plus `/health` and `/alive`.
  Only the last two skip the `X-Balance-Secret` check; every other request needs the header equal to
  `Balance:SharedSecret` (constant-time compare), else 401 with no detail. The secret is required at startup and
  at least 32 characters. Responses are camelCase JSON, enums as strings, gzip, no NaN or Infinity (`null`).
- **Limits:** one run executes at a time and 3 wait; one more gets 429. `runsPerRow` is at most 1000, overrides at
  most 500 keys, the request body at most 1 MiB (Kestrel, 413). A finished run is kept 1 hour, then 404, and at
  most 100 finished runs are retained. The queue is in memory, so the chart runs one replica.
- **Export** (`POST /exports`): branches `balance/<slug>-<yyyyMMdd-HHmm>` from the commit the service was built
  from, commits only the changed `balance/*.json` files in `ConfigFiles.Save` canonical form, and opens a draft PR
  against `main` titled `chore(balance): <title>` with a `Player note: No gameplay changes: balance tuning
  proposal.` line. It uses `Balance:GitHubToken`, a fine-grained token, never logged; without it exports answer
  503. The build commit is the `+<sha>` of `AssemblyInformationalVersion`, so CI publishes the service with
  `-p:SourceRevisionId="$(git rev-parse HEAD)"` (the checked-out commit, not `github.sha`, which is main's head on a manual release) on a publish that builds (no `--no-build`); without it exports answer 503.
- **Seed data:** read from `WorldDbContext`'s design-time model (`HasData`, no database); the `balance/*.json`
  files are copied next to the binary (`/app/balance` in the image) by the csproj.
- **API side:** `Application:Balance:Url` and `Application:Balance:SharedSecret`, read by the worlds service only, so
  only a process that runs worlds holds the secret. Unconfigured (either empty) the
  `/balance/*` endpoints answer 503 ProblemDetails (`BalanceUnavailableException`) and the rest of the API is
  unaffected. The typed client removes the standard resilience handler, retries only GET (10 s an attempt) and gives
  POST and DELETE a single 60 s attempt with no retry, so a run or an export is never started twice. A 401 from the
  service maps to 502: it is our misconfiguration, not the caller's.
- **Deploy:** chart `avalon-balance` (image `ghcr.io/woozchucky/avalon-server/balance`), secret keys
  `balance-shared-secret` and optional `github-token`; the api chart takes `balance.url` and the same
  `balance-shared-secret` key, both rendered only into a release that runs worlds. The homelab wiring is a separate
  change.
- **Local dev:** the Aspire AppHost (`src/Server/Avalon`) adds `balance` with a generated, persisted `balance-secret`
  parameter passed to both the service and the api, and sets the api's `Application__Balance__Url` from the
  service's endpoint (`http://localhost:5220`). Running the service alone needs `Balance__SharedSecret` (32+
  characters, e.g. in user-secrets or the environment).
