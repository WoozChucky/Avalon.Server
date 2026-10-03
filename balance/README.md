# Balance simulator

`tools/Avalon.Balance` answers "what do the current numbers produce" (kill times, death times, win rates, class
parity, level curve) and "does this change make it better", before a migration is written. It reads the World seed
data from the EF model (`HasData`), applies `overrides.json`, simulates one player against packs of forest creatures,
and grades the results against `targets.json`.

## Structure

- `src/Server/Avalon.Balance.Core`: the simulator, the grader and the run contract (`Simulation.Run` takes a seed, a
  config and a `RunRequest` and returns a `RunResult`; a refusal is an `Issue` with a path, never an exception;
  progress and cancellation are built in). It references `Avalon.Combat` and `Avalon.Domain` only, so it can run inside
  a service. `Catalog.Describe` lists every value an override accepts; `ConfigFiles.Save` writes the config files.
- `src/Server/Avalon.Balance.Data`: reads the seed from the EF model and the files from disk, for Core.
- `tools/Avalon.Balance`: the command line, a thin wrapper over the two.

## Running

```bash
dotnet run -c Release --project tools/Avalon.Balance
dotnet run -c Release --project tools/Avalon.Balance -- --class Warrior --scenario normal-3 --runs 5000
dotnet run -c Release --project tools/Avalon.Balance -- --overrides balance/try-slam.json --seed 1
```

Output: `balance/out/report.html` and `balance/out/results.csv` (never committed). Exit code 0 when no graded row is
red, 1 when one is, 2 when the input was refused (a bad option, override or config file; the message names it).
A full run (4 classes x 10 levels x 3 gear x 8 scenarios x 1000 runs, 960 rows) takes about 20 s in Release on a
desktop; narrow it with `--class`, `--scenario` and `--runs` while iterating. The same seed gives the same report on any machine.

## Files

- `overrides.json`: a flat map `"Table.key.Column": value`, empty on `main`. Tables and keys:
  `Ability.<id>`, `ClassLevelStat.<Class>.<level>`, `ClassStatFactors.<Class>`, `CombatFormula` (no key),
  `CreatureBaseStats.<level>`, `CreatureRarityModifiers.<Rarity>`, `CreatureTemplate.<id>`, `Item.<id>`, `Aura.<id>`,
  `AuraStatModifier.<auraId>.<Stat>`.
  Example: `{ "Ability.201.EffectValue": 18, "CombatFormula.ArmorBase": 60 }`. An unknown table, key or column stops
  the run naming it, as does a key given twice or a computed column; a value the server's validators would refuse
  stops it with their message. An override equal to the seed is reported as stale ("already applied"): delete it.
- `scenarios.json`: runs, seed, the level range `[first, last]`, classes, gear profiles to run, the scenarios (a pack
  entry names a `rarity`, drawn per run from the hostile templates of that rarity, or a `template` id; `levelOffset`
  is added to the player's level and clamped to the template's range, or `"template"` rolls the template's own range;
  `coneHits` caps how many creatures a cone reaches), and `gearProfiles` (item ids per class). `forest` is graded.
- `targets.json`: per scenario, bands for win rate (%), median fight length (s) and median health left on wins (%);
  the global checks (flat curve across levels, class parity, resource flow, levelling pace). Green inside a band,
  yellow within `yellowTolerancePct` of the edge crossed, red beyond or when there is no value. Every scenario it
  names must exist in `scenarios.json`.
- `rotations.json`: per class, a priority list; the first entry that is off cooldown, affordable and whose conditions
  hold is cast. Conditions: `targetsAlive`, `healthPct`, `power`, `powerPct`, each like `">=2"`.

Every config file refuses a field it does not know, so a typo stops the run instead of being ignored.
`scenarios.json`, `targets.json` and `rotations.json` are kept in the canonical form `ConfigFiles.Save` writes, so
saving a file unchanged changes nothing; `overrides.json` is yours to format. Save with `ConfigFileStore.Write`.

## What the simulator models, and what it does not

- Every number comes from the server's code, the rules in `Avalon.Combat`: `CharacterStatsCalculator`,
  `CreatureStatDeriver`, `HitResolver`, `Haste`, `AbilityCost`, `AbilityAmounts`, `PowerRegen`, `PowerPool` (power
  gains and which pool a reset empties), `Fury.FromDamageTaken` (Fury from damage taken) and `HealRules` (a heal's
  cap); the data checks (`AbilityRules`, `CombatDataRules`, `CreatureTemplateRules`) are shared the same way. Only the
  order of events in a tick is the simulator's own, and `SimulatorParityShould` (World unit tests) pins it against
  `CombatService` and the cast system.
- **Creature kits are a table.** `CreatureKits.ByScript` in Core lists each script's basic, specials and ranged-only
  specials, because Core does not read `Avalon.World`'s scripts. A new or changed creature script needs its row
  updated; `CreatureKitParityShould` (World unit tests) fails when the table and the scripts differ.
- One step is one server tick (1/60 s). Mana and Energy regenerate at the in-combat rate (`stat x 0.05` a second),
  the fraction of a point carried between ticks as the server carries it, unless a cast-time cast was in progress
  within the last 5 s.
- **Everyone is in melee.** Circles and cones hit every living creature (a cone at most `coneHits` of them), a
  projectile the first (or all when it pierces). There is no movement, so nobody walks, kites or steps out of a
  telegraph.
- **Projectile travel time is outside the model.** A player's projectile lands at the end of the cast phase of the
  tick it is fired, a creature's one tick later. On the server a projectile flies at its `ProjectileSpeed`, so even
  at melee distance the first one lands a few ticks later than the simulator's.
- **Combat is continuous.** Health does not regenerate, Mana and Energy regenerate at the in-combat rate, and Fury
  never decays out of combat, because a fight never leaves combat. That holds while hits land at least every 5 s
  (the server's combat tag); a kiting player or a ranged opener that leaves gaps longer than that is not modelled.
- **Creature wind-ups always land**: nobody dodges a telegraph. Creatures use their script's kit, the first ready
  special whose reach fits, else the basic; the Blightfly never casts Blight Spit, since its script spits only while
  Sting cannot reach, and in melee it always can.
- **Creature levels end at 5.** No seeded hostile template's level range goes past 5 (several stop sooner), and a
  `levelOffset` is clamped to the template's range, so the "same level" and "+2" rows above that fight capped
  creatures: from level 6 up they grade how the player outgrows the forest, not a fair fight.
- **Auras** (damage and healing over time, and stat auras) are modelled through the server's rules (`AuraRules`,
  `AuraSchedule`, `AuraStats`, `HitResolver.ResolvePeriodic` and `ResolvePeriodicHeal`): an ability's aura goes on
  every unit its hit landed on (none after a dodge, and on the caster for an Ally ability), its base damage roll drawn
  right after the hit's; it ticks on the server's schedule right after the cast phase, each tick with one crit roll and
  the fraction of a point carried to the next; and it folds its modifiers into stats as `CharacterStatsCalculator`
  does. A death ends every aura the unit holds. Movement speed modifiers change nothing (nobody moves), the cap on
  auras per unit is never reached, no aura script runs, and the server's catch-up after a stall does not arise (the
  simulator never falls behind).
- Not modelled: movement, dodging wind-ups, potions and other consumables, groups, PvP, threat, out-of-combat health
  regeneration and Fury decay, and the map's experience band.
- A fight ends when the player dies, every creature dies, or at 300 s (a loss).

## Turning an override into a change

1. Put the new value into the `HasData` call in `src/Server/Avalon.Database.World/WorldDbContext.cs` (or
   `Seeding/CombatSeed.cs` for the formula and class factors). Never edit balance rows with a raw
   `migrationBuilder.Sql`.
2. Generate the migration (see CLAUDE.md, "Add a migration"), with a placeholder connection string:
   `Database__World__ConnectionString="Host=127.0.0.1;Port=1;Database=design_time_only" dotnet ef migrations add <Name> --project src/Server/Avalon.Database.World --startup-project src/Server/Avalon.Api --context WorldDbContext`
3. Remove the entry from `overrides.json`; the next run reports any leftover as stale.
4. `ModelDriftShould` fails if step 2 was skipped.

The balance workbench, the admin page that runs the simulator through `Avalon.Balance.Service` and exports a tuning
proposal as a draft pull request, edits these files from the dashboard; this folder stays the source of truth, and an
export commits only the changed `balance/*.json` files.
