# Balance simulator and Balance Service

The balance simulator library and tool, and the in-cluster service that runs it for the workbench. File formats are in `balance/README.md`.

## Balance Simulator

**What the simulator mirrors (#1183).** The simulator runs the world's combat core. Every number in a fight comes from
the shared `Avalon.Combat` and `Avalon.Domain` code the world server runs, and the bookkeeping it keeps itself is held
to the world's by `SimulatorParityShould`. That covers the class-neutral stats and defences (evasion from Agility, no
class base dodge or block, armour from Strength, the formula's life per Stamina and base crit for every class, the
creatures' evasion seed) and the offence model of #1178 (each hit's typed parts and each heal from `OffenceMath`, the
offence block from `CharacterStatsCalculator`, cooldowns and cast times from `AbilityPace` over the three speeds:
attack speed, cast speed and cooldown recovery), so an element meets its resistance. It also covers these:

- **Gear is fixed stats.** Each item a gear profile in `balance/scenarios.json` names is worn as one fixed copy of its
  template, made once when the data is loaded (`FixedGear`, `BalanceData.Worn`) and worn by every row. Every number on
  it sits at the midpoint of its range, `min + (max + 1 - min) / 2`: the low end plus half the width, rounded down,
  so a half rounds up, below zero too ([-3, -1] gives -2). Its base stats (a base's armour,
  evasion, ward or block, or a weapon's damage range and attack time) come from its template's `ItemTemplateBaseStats`
  rows, a unique's from its named base's. Its modifiers are one per modifier the template links
  (`ItemModifierCatalog.LinkedTo`): the base's implicit first, then a unique's own. Each stat line sits at its
  midpoint, and a line's second value, where it has one, is never below its first. Its damage type is
  `WornItem.DamageTypeOf`. That copy is the world's plain roll (`ItemRoller.RollPlain`, a copy that rolls no affix,
  only its base stats and implicit) under a random that always lands mid-range, and `SimulatorParityShould` holds it
  to that. No affix, rarity or item level is rolled, since rolled gear is outside the model. The profiles name bases;
  the Cleric wears the cloth sets (41-45 and 17-21) beside its maces (35 and 8).
- **A ward on both sides.** A ward is a pool in front of health (#1178). A player's and a creature's are the world's
  `WardPool`: full when the unit enters the fight, keeping its share when a stats refresh or an aura fold moves its
  maximum, and empty at death. A direct hit gives the ward what the hit lets it absorb (`HitDamage.Absorbable`,
  everything but the hit's blight part), pauses its recharge and takes the rest from health, as
  `CombatService.ResolveHit` does. An aura's tick meets the ward only when `HitResolver.WardAbsorbs` allows the tick's
  type, so never blight and never physical damage over time (a bleed), and any tick pauses the recharge. The recharge
  runs on the fight's clock with the default regeneration settings (`RegenConfiguration`). A row's damage dealt and
  taken count what the ward absorbed plus the health removed.
- **Aura ticks take their source's type.** An aura is typed when it is placed, as `AuraSystem.Apply` types it: its
  ability's damage type, else the caster's base type (`HitResolver.TypeOf`). Each tick resolves from the aura's
  snapshot (`HitResolver.ResolvePeriodic` and `ResolvePeriodicHeal`), so a fire aura meets fire resistance and a ward,
  and a bleed meets armour and passes the ward.
- **Target limits.** The pack stands in the nearest-first order of the world's hit queries. A circle hits the first
  living creatures up to its `MaxTargets`, a cone the same and at most the scenario's `coneHits`, and a projectile the
  first creature, or, when it pierces, every creature up to its `MaxTargets`. A `MaxTargets` of 0 sets no limit.
- **The item rows are checked as the world checks them.** When the data is loaded, before any row runs, each item
  template is held to the rules the world's items area holds it to: its links against its rarity
  (`ItemModifierCatalog.RarityRefusal`), its base stats against its kind (`ItemBaseStatRules.Problem`), and a unique's
  named base (`ItemBaseStatRules.NamedBaseProblem`). The modifier catalog is built with the shared
  `ItemModifierCatalog`, and a row it would log and leave out stops the run. A modifier line whose range runs
  backwards, or whose second range is set at one end only, is refused too, since the world could not roll it. Each
  refusal is a `seed` issue, in the world's words but for the line ranges, which only the simulator checks, so an
  override that makes a row invalid stops the run before it starts, never midway.

The next part of #1183 is builds: characters as talent allocations, with their stat lines, abilities, capstone shapes
and keystone rules.

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
is applied to a copy of it. It never copies a balance formula: stats, hits, speeds, costs, mana regeneration, mana
gains and heal caps (`ManaRegenRates`, `PowerRegen`, `PowerPool`, `HealRules`, see World Simulation) are
`Avalon.Combat`, the code the world server runs, and so are the checks that validate the data: `AbilityRules`,
`CombatDataRules` and `CreatureTemplateRules` in `Avalon.Combat`, which the world's
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
`CombatService`, the cast system and `CharacterEntity.Update`. Hits follow `AbilityEffect.Apply`: every Hostile
ability rolls whether it lands, damage or not (#1164, `CombatRules.Lands` for one without `Damage`; none for an
`Unavoidable` one), and its aura lands only where it landed, so an aura-only ability's simulated output (Ignite,
Sundering Howl) drops with the target's chance to evade it. It steps at the server's tick (1/60 s) so cooldowns,
casts and the event order count as the server's do. Every class spends mana (#1181), which regenerates at the in-combat
rate, `ManaRegenRates.InCombat x dt` a tick (1 % of the maximum a second), with the fraction of a point carried between
ticks (`PowerRegen.TakeWholePoints`, the caller owning the carry), dropped when the pool cannot regenerate (full
included); a cast never pauses it.
`ConfigFiles.Save` writes `scenarios.json`, `targets.json` and `rotations.json` in one canonical JSON form
(`ConfigFileStore.Write`), and the checked-in files are in that form, so a parse and save round-trips byte for byte.
Balance tables change only through `HasData` plus a migration (see Commands); `ModelDriftShould` fails when `HasData`
changed without one. `balance/README.md` has the file formats and what is not modelled (everyone in melee, no
projectile travel time, continuous combat, wind-ups always land, no crowd control).

**Crowd control is not modelled in fights** (owner decision, #770): nothing stuns, roots, silences, resists,
diminishes or breaks early there. The seed's crowd-control columns are still read and validated with the world's rules
(an aura's `Control` and break chances through `AuraRules.Problem`, the creature and rarity duration percentages and
the rarity's crit and block chances (#1145) through `CreatureTemplateRules.Validate` and `ValidateRarities`), so
an override the world would refuse is refused here too, and the overrides reach them like any column. The four crowd-control abilities (204, 214, 224, 235) are
in no rotation in `balance/rotations.json`.

**A simulated character holds its class's starter alone (#1181).** Since the talent tree, a new character holds only
the ability its class start links, and every other ability is a node it walks to. Until builds land (the next part of
#1183) the simulator models no allocation, so `TalentTree.Starter` holds the starter alone, through the start's link
in the seed's `TalentClassStarts` and `TalentNodeAbilities` (Heavy Strike, Spark Bolt, Quick Shot, Smite), and a
rotation entry naming any other ability is refused when the rotation is compiled (`RotationFile.Compile`: "is not in
the class's kit"). So the checked-in `balance/rotations.json` names each class's starter alone, one entry with no
condition. Whether an ability is a class ability (`granted`, for the ability and aura rules) is read from the same
links. The Balance Service's run tests (`RunQueueShould`, `RunEndpointsShould`, `ExportEndpointsShould`,
`WireMappingShould`) run on a fixture rotation, each class casting its starter as the seed gives it
(`BalanceTestHost.FixtureHost`), so neither the checked-in rotations nor the seed's kits can stop them. A simulator
test that scripts more than the starter gives the character those abilities directly (`TestData.Holding` in the
balance unit tests; the world's parity tests add each `SimAbility` themselves).

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
  503. The build commit is the `+<sha>` of the assembly's `AssemblyInformationalVersionAttribute` (`BalanceHost`), so CI publishes the service with
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
