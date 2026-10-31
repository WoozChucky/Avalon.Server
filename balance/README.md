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
dotnet run -c Release --project tools/Avalon.Balance -- --build hunter-double-tap --runs 200
dotnet run -c Release --project tools/Avalon.Balance -- --overrides balance/try-slam.json --seed 1
```

Output: `balance/out/report.html` and `balance/out/results.csv` (never committed). Exit code 0 when no graded row is
red, 1 when one is, 2 when the input was refused (a bad option, override or config file; the message names it).
A full run (8 builds x 10 levels x 3 gear x 8 scenarios x 1000 runs, 1920 rows) takes about a minute in Release on a
desktop; narrow it with `--class`, `--build`, `--scenario` and `--runs` while iterating. The same seed gives the same report on any machine.

## Files

- `overrides.json`: a flat map `"Table.key.Column": value`, empty on `main`. Tables and keys:
  `Ability.<id>`, `ClassLevelStat.<Class>.<level>`, `CombatFormula` (no key, which holds the mana terms
  `ManaBase`, `ManaPerLevel` and `ManaPerIntellect` since #1181), `CreatureBaseStats.<level>`,
  `CreatureRarityModifiers.<Rarity>`, `CreatureTemplate.<id>`, `Item.<id>`, `Aura.<id>`,
  `AuraStatModifier.<auraId>.<Stat>`, `ItemTemplateBaseStat.<templateId>.<Stat>` (a base's range for one base stat,
  `Min` and `Max`), `ItemModifierStat.<modifierId>.<index>` (a modifier's stat line, by its index),
  `TalentNodeStat.<nodeId>.<Stat>.<Kind>` (a talent node's stat line, `Value` and `ValueMax`) and
  `TalentAbilityModifier.<nodeId>.<Kind>` (a capstone's modifier, `Value` and `Value2`). A talent line or capstone
  modifier the world's talent catalog would refuse stops the run with its words.
  Example: `{ "Ability.201.Cost": 18, "CombatFormula.ArmorBase": 60 }`. An unknown table, key or column stops
  the run naming it, as does a key given twice or a computed column. A key names an enum part by its name only
  (`ClassLevelStat.Warrior.1`, never a number), and an enum value must be one the type defines. A value the server's
  validators would refuse stops it with their message, and so does a modifier line whose range runs backwards. An
  override equal to the seed is reported as stale ("already applied"): delete it.
- `scenarios.json`: runs, seed, the level range `[first, last]`, `builds` (the ids in `builds.json` to run, each named
  once; class parity takes each class's best build by the highest win rate, on a tie above 0 % the shorter median
  fight and at 0 % the longer, and on a full tie the build named first here), `bonusTalentPoints` (the talent points a character holds beyond its
  level, 0 or more: 3 checked in, the three seeded paying quests), gear profiles to run, the scenarios (a pack
  entry names a `rarity`, drawn per run from the hostile templates of that rarity, or a `template` id; `levelOffset`
  is added to the player's level and clamped to the template's range, or `"template"` rolls the template's own range;
  `coneHits` caps how many creatures a cone reaches), and `gearProfiles` (item ids per class). `forest` is graded.
- `targets.json`: per scenario, bands for win rate (%), median fight length (s) and median health left on wins (%);
  the global checks (flat curve across levels, class parity, resource flow, levelling pace). Green inside a band,
  yellow within `yellowTolerancePct` of the edge crossed, red beyond or when there is no value. Every scenario it
  names must exist in `scenarios.json`.
- `builds.json`: build id to `{ "class", "nodes", "rotation" }`. `nodes` is the talent path, node ids in the order they
  are taken; at level `L` a row holds the first `L + bonusTalentPoints` of them (all of them when the path is
  shorter). The whole path must pass the world's allocation rules node by node (a node in the tree, not taken twice
  or a start, next to the class's start or a node taken earlier, its fork partner not taken), or the build is refused
  naming the node and the reason (`node 3: NotConnected`). `rotation` is a priority list of `{ "ability", "when" }`:
  the first entry that is held at the row's level, off cooldown, affordable and whose conditions hold is cast; an
  entry not held yet is passed over and never counted as starved. Conditions: `targetsAlive`, `healthPct`, `power`,
  `powerPct`, each like `">=2"`. An entry naming an ability the whole path never holds (the class's starter included),
  an unknown class or an empty rotation is refused. The checked-in file holds eight builds, a capstone build and a
  keystone build per class (`docs/balance.md` lists them).

Every config file refuses a field it does not know, so a typo stops the run instead of being ignored.
`scenarios.json`, `targets.json` and `builds.json` are kept in the canonical form `ConfigFiles.Save` writes, so
saving a file unchanged changes nothing; `overrides.json` is yours to format. Save with `ConfigFileStore.Write`.

## What the simulator models, and what it does not

- Every number comes from the server's code, the rules in `Avalon.Combat`: `CharacterStatsCalculator`,
  `CreatureStatDeriver`, `HitResolver`, `AbilityPace` (cooldowns and cast times over a `CasterPace`), `AbilityCost`,
  `AbilityAmounts`, `ManaRegenRates` (the mana regeneration rates), `PowerRegen`, `PowerPool` (mana gains),
  `HealRules` (a heal's cap) and `WardPool` (the ward); the data checks (`AbilityRules`, `CombatDataRules`,
  `CreatureTemplateRules`, and `Avalon.Domain`'s `ItemBaseStatRules` and `ItemModifierCatalog`) are shared the same
  way, and so are the talent rules (`TalentTotals`, `AbilityShaping`, `TalentStatRules`, `TalentModifierRules`,
  `AuraRules.Lengthen` and `NextStacks`). These are the simulator's own: the order of events in a tick (second hits,
  fan lanes and explosions among them), the fixed copy of each item (`FixedGear`), the ward's handling around a hit or
  a tick (`CombatRules.Land` and `LandTick`), the allocation rules a build's path is checked by and the order of its
  abilities (`TalentTree.Hold`). `SimulatorParityShould` (World unit tests) pins each against the world: the order
  against `CombatService` and the cast system, the copies against the world's item roll at the midpoint, the ward
  against `CombatService`'s hits and aura ticks, and the builds against `TalentRules.Allocate`,
  `TalentAbilities.Rebuild` and a stats refresh of a character entered on the same path.
- **Characters are talent builds** (`builds.json`): each row holds its build's path up to its level, its stats folding
  the held nodes' lines, its abilities the tree's (the class start's starter, then each held ability node's), each
  shaped by the held capstones. A second hit lands the share with its own rolls and the aura once; a fan's lanes share
  one set of creatures hit, each lane the first in pack order the cast has not hit; an explosion reaches every other
  living creature, in pack order, at its share with the Area tag, damage only. The aura capstones lengthen and deepen
  their aura, never crowd control's. Blood Price pays costs from life, Steady Hand never crits, Unbroken's ward takes
  everything and recharges after twice the delay, and Glass Edge is stat lines.
- **Creature kits are a table.** `CreatureKits.ByScript` in Core lists each script's basic, specials and ranged-only
  specials, because Core does not read `Avalon.World`'s scripts. A new or changed creature script needs its row
  updated; `CreatureKitParityShould` (World unit tests) fails when the table and the scripts differ.
- One step is one server tick (1/60 s). Every class spends mana (#1181), which regenerates at the in-combat rate (1 %
  of the maximum a second, `ManaRegenRates.InCombat`), the fraction of a point carried between ticks as the server
  carries it; a cast never pauses it.
- **Gear is fixed stats.** Each item a gear profile names is worn as one fixed copy (`FixedGear`), made once and worn
  by every row: its base stats (armour, evasion, ward, block, or a weapon's damage range and attack time) and its
  linked modifiers (a base's implicit, a unique's own), every value at the midpoint of its range,
  `min + (max + 1 - min) / 2`: the low end plus half the width, rounded down, so a half rounds up, below zero too.
  It is the copy the world makes of an item that rolls no affix (its plain roll, base stats and implicit only) under
  a random that always lands mid-range. No affix, rarity or item level is rolled.
- **Ward** is modelled on both sides through the server's `WardPool`: full at the start of a fight, in front of health,
  emptied at death. A direct hit's ward takes everything but its blight part; a tick meets the ward only when
  `HitResolver.WardAbsorbs` allows its type (never blight, never a bleed); any damage pauses the recharge. Damage dealt
  and taken count what the ward absorbed plus the health removed.
- **Everyone is in melee.** The pack stands nearest first. Circles and cones hit the first living creatures up to the
  ability's `MaxTargets` (a cone also at most `coneHits` of them), a projectile the first, or when it pierces every
  creature up to its `MaxTargets`; a `MaxTargets` of 0 sets no limit. There is no movement, so nobody walks, kites or
  steps out of a telegraph.
- **Projectile travel time is outside the model.** A player's projectile lands at the end of the cast phase of the
  tick it is fired, a creature's one tick later. On the server a projectile flies at its `ProjectileSpeed`, so even
  at melee distance the first one lands a few ticks later than the simulator's.
- **Combat is continuous.** Health does not regenerate and mana regenerates at the in-combat rate, because a fight
  never leaves combat. That holds while hits land at least every 5 s (the server's combat tag); a kiting player or a
  ranged opener that leaves gaps longer than that is not modelled.
- **Creature wind-ups always land**: nobody dodges a telegraph. Creatures use their script's kit, the first ready
  special whose reach fits, else the basic; the Blightfly never casts Blight Spit, since its script spits only while
  Sting cannot reach, and in melee it always can.
- **Creature levels end at 5.** No seeded hostile template's level range goes past 5 (several stop sooner), and a
  `levelOffset` is clamped to the template's range, so the "same level" and "+2" rows above that fight capped
  creatures: from level 6 up they grade how the player outgrows the forest, not a fair fight.
- **Auras** (damage and healing over time, and stat auras) are modelled through the server's rules (`AuraRules`,
  `AuraSchedule`, `AuraStats`, `HitResolver.ResolvePeriodic` and `ResolvePeriodicHeal`, each tick typed as its
  ability's hit, else by the caster's base type): an ability's aura goes on
  every unit its hit landed on (none after a dodge, which an ability without damage rolls too unless it is
  Unavoidable, and on the caster for an Ally ability), its base damage roll drawn
  right after the hit's; it ticks on the server's schedule right after the cast phase, each tick with one crit roll and
  the fraction of a point carried to the next; and it folds its modifiers into stats as `CharacterStatsCalculator`
  does. A death ends every aura the unit holds. Movement speed modifiers change nothing (nobody moves), the cap on
  auras per unit is never reached, no aura script runs, and the server's catch-up after a stall does not arise (the
  simulator never falls behind).
- Not modelled: crowd control (#770: stuns, roots, silences, slows, resistance, diminishing and early breaks; the four
  crowd-control abilities are in no build's rotation, and Crippling Shot's slow is placed but does nothing and never breaks
  early), area, reach and arc capstones change no target (everyone is in melee),
  a refresh's later expiry (one caster per aura), rolled gear (affixes, rarities and item levels), Resonance effects (the procs
  worn affix families trigger), Awakening (a sealed affix that wakes as it is used), movement, dodging wind-ups,
  potions and other consumables, groups, PvP, threat, out-of-combat health and mana regeneration, and the map's
  experience band.
- A fight ends when the player dies, every creature dies, or at 300 s (a loss).

## Turning an override into a change

1. Put the new value into the `HasData` call in `src/Server/Avalon.Database.World/WorldDbContext.cs` (or
   `Seeding/CombatSeed.cs` for the formula). Never edit balance rows with a raw
   `migrationBuilder.Sql`.
2. Generate the migration (see CLAUDE.md, "Add a migration"), with a placeholder connection string:
   `Database__World__ConnectionString="Host=127.0.0.1;Port=1;Database=design_time_only" dotnet ef migrations add <Name> --project src/Server/Avalon.Database.World --startup-project src/Server/Avalon.Api --context WorldDbContext`
3. Remove the entry from `overrides.json`; the next run reports any leftover as stale.
4. `ModelDriftShould` fails if step 2 was skipped.

The balance workbench, the admin page that runs the simulator through `Avalon.Balance.Service` and exports a tuning
proposal as a draft pull request, edits these files from the dashboard; this folder stays the source of truth, and an
export commits only the changed `balance/*.json` files.
