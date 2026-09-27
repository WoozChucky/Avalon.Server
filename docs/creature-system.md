# Creature System

This document describes a creature's life: its template and stats, how it is placed in an
instance, the AI script it runs, how it fights and dies, and what happens to its corpse. Creatures
do not respawn.

The authoritative summary lives in the World Simulation section of [CLAUDE.md](../CLAUDE.md). This
document follows one creature through the code.

---

## Templates and Stats

A creature type is a `CreatureTemplate` row in the World database (`Avalon.Domain.World`). The
template implements `ICreatureMetadata` (`Avalon.World.Public.Creatures`), the read-only view that
scripts see. Every creature of a type shares the same template object, so `ICreatureMetadata` has no
setters. Per-creature state lives on `ICreature`.

### Stats are derived at spawn

`CreatureSpawner.Spawn` rolls a level between the template's `MinLevel` and `MaxLevel` (at least 1),
then asks `CreatureStatDeriver.Derive` for the stats that level gives:

1. Look up the `CreatureBaseStats` row for the level: `Health`, `DamageMin`, `DamageMax` and
   `Experience`. A level with no row logs a warning and uses the highest seeded level.
2. Scale health by the template's `HealthModifier`, damage by `DamageModifier`, and experience by
   `ExperienceModifier`.
3. Scale again by the template's `Rarity`, through its `CreatureRarityModifiers` row
   (`HealthMultiplier`, `DamageMultiplier`, `ExperienceMultiplier`). A rarity with no row logs a
   warning and is not scaled.
4. Floor health and minimum damage at 1, and keep the maximum damage at or above the minimum.

A modifier or multiplier of 0 or less counts as 1, so a zero in seed data does not zero a stat.

| Rarity | Value |
|---|---|
| `Normal` | 0 |
| `Elite` | 1 |
| `Rare` | 2 |
| `Boss` | 3 |

Rarity is set per template, not rolled per spawn.

### The `Exp` override

`CreatureTemplate.Experience` (column `Exp`) is nullable:

- `null` means derive the experience as above.
- Any value, 0 included, is used as written. It replaces the whole derivation, modifiers and rarity
  included.

The result is stored on the creature as `ICreature.Experience`. That is what a kill awards, not
`ICreatureMetadata.Experience`, which is only the override.

### What else the spawner sets

`CreatureSpawner.Spawn` also sets the creature's `Name`, its speed (the template's `SpeedWalk`),
`ScriptName`, `Invulnerable`, `BodyRadius` (the default radius when the template's is not a finite
value above 0), and `CanInteract`, fixed at spawn from the dialogue catalog. Creatures cannot cast,
so `Power` is 0.

`CreatureSpawner.Spawn` runs on the thread pool during instance construction. It reads
`world.Data.Creatures` once, so a reload of the creatures area landing mid-spawn cannot pair a new
template with an old deriver.

---

## Placement

`ChunkLayoutInstanceFactory.BuildAsync` places creatures through `ICreaturePlacementService`
(`Avalon.World/ChunkLayouts`). There are two paths.

| Path | Runs for | Source |
|---|---|---|
| `PlaceAsync` | Procedural layouts only | A `SpawnTable`, rolled against the chunks' spawn slots |
| `PlaceAuthoredAsync` | Every layout, town and procedural | The map's `MapCreatureSpawn` rows |

### Procedural: `PlaceAsync`

For each chunk spawn slot, skipping the `empty` and `entry` tags, the service picks one
`SpawnTableEntry` with the slot's tag, weighted by `Weight`, and spawns between `MinCount` and
`MaxCount` of that creature. A single creature stands on the slot's centre. A pack is spread at
random within 1.5 m of it on each axis. The random seed is the layout's seed.

### Authored: `PlaceAuthoredAsync`

Each `MapCreatureSpawn` row names a creature template, an offset (`OffsetX`, `OffsetY`, `OffsetZ`)
from the map's entry spawn point, and a `Facing`. `OffsetY` only centres the navmesh search:
`SampleGroundHeight` sets the real height. This is the only path that puts a creature in a town.

### Patrol paths

A `MapCreatureSpawn` can name a `CreaturePath` through `PathId`. A path holds ordered
`CreaturePathPoint` rows (`Sequence`, an offset, `WaitMs`). `PlaceAuthoredAsync` turns each point into
a world position snapped to the navmesh, the same way as the spawn, and sets `ICreature.PatrolPath`
before it attaches the script.

The path does not choose the AI. Only a script that reads `PatrolPath` walks it. Today that is
`CreaturePatrolScript`, which loops through the points and waits at each one for its `WaitMs`.
Procedural spawns never get a path.

### One bad row costs one creature

Both paths wrap each creature in its own `try`/`catch` and log the error. Placement runs inside
instance construction, so a throw would make the map unenterable for everyone.

---

## Scripts

### Binding by name

A template's `ScriptName` names an `AiScript` subclass. At `Load()`, `ScriptManager` finds every
concrete `AiScript` subclass and keys it by its type name. So a grep for the class name proves
nothing about whether a script is used.

`CreaturePlacementService.AttachScript` looks the name up and builds the script:

```csharp
ActivatorUtilities.CreateInstance(_sp, scriptType, creature, instance)
```

- An empty `ScriptName` attaches nothing.
- An unknown name logs a warning and attaches nothing.
- A constructor that throws is caught and logged as an error. The creature is left with no script.

### The constructor rule

`AttachScript` passes exactly two runtime arguments: the creature and the instance (as
`ISimulationContext`). Anything else must come from DI. `ILoggerFactory` is fine. A plain `float` or
array is not, and the creature silently ends up with no script. `AiScriptConstructibilityShould`
builds every nameable script this way against the production container.

### `[ChainedScript]`

A script that another script builds and chains, with extra arguments, is marked `[ChainedScript]`
(`Avalon.World.Public/Scripts`). `ScriptManager` does not register it by name, so a template naming
it gets "not found" rather than a script that fails to construct.

### The scripts

| Script | Nameable | Behaviour |
|---|---|---|
| `AggroDefendScript` | Yes | Stands at its spawn. Chains a range detector and a `CreatureCombatScript`. The aggro range is the template's `DetectionRange`, or 10 m when that is 0. |
| `CreatureCombatScript` | Yes | Chases, attacks and returns home (see Combat). |
| `CreaturePatrolScript` | Yes | Walks `ICreature.PatrolPath` in a loop. Stands still when there is no path. |
| `TownNpcScript` | Yes | Does nothing. Town NPCs run it. |
| `CreatureRangeDetectorScript` | No, `[ChainedScript]` | Checks once a second for a living character within range and in line of sight. |

### Hooks

`AiScript` has these hooks. By default each one forwards to the chained scripts.

| Hook | Called when |
|---|---|
| `Update(deltaTime)` | Every instance tick, for every creature with a script |
| `OnHit(attacker, damage)` | The creature is hit: `Creature.OnHit` forwards to its script |
| `OnEnteredRange(character)` | A range detector reports a character |
| `OnCharacterLeft(character)` | A character left the creature's instance (#546) |

`OnCharacterLeft` is a notification only and grants nothing. `MapInstance.RemoveCharacter` calls it
on every creature in the instance, after the character is no longer a member. Each call is
contained: a script that throws is logged, and the other scripts still hear it.
`CreatureCombatScript` uses it to drop a target that left: it releases its melee slot, heals to full
and returns home.

---

## Movement

Scripts choose destinations. `ICreatureLocomotion` is the only thing that writes a creature's
position, and `MapInstance` ticks it after the creature scripts. `GameConfiguration.CreatureLocomotion`
picks `Waypoint` (the default) or `Crowd`.

`MeleeSlots` gives each attacker its own position on a ring around the target (`MeleeSlotCount`,
default 6; `MeleeSlotRadius`, default 1.5 m). Attackers past the count stand off at attack range.

See the World Simulation section of [CLAUDE.md](../CLAUDE.md) for the locomotion and melee-slot
contracts.

---

## Combat

### `CreatureCombatScript`

| State | Behaviour |
|---|---|
| `None` | Idle. `OnEnteredRange` or a hit starts combat and records where the fight started. |
| `Combat` | Follows the encounter's top threat, or the taunter while a taunt lasts. Moves to its melee slot and attacks when within 1.5 m. |
| `Returning` | Runs home. A hit in this state does no damage. |

- An attack every 2.25 s rolls damage between the creature's `DamageMin` and `DamageMax`, and goes
  through `ICombatService.ApplyDamage`.
- It gives up and returns home at full health when the target dies, leaves the instance, or the
  creature is more than 40 m from where the fight started.
- If no path home is found, the creature is teleported home.

A creature takes damage only through its script's `OnHit`, so a creature with no script takes none.

### `CombatService`

Every hit, a creature's swing or a player's skill, goes through the instance's `CombatService`
(`Avalon.World/Combat`):

1. An `Invulnerable` target returns at once: no damage, no encounter, no threat, no combat tag.
2. The attacker and the target join an encounter. Threat is added when the target is a creature.
3. The hit is applied. When a creature goes from above 0 health to 0, the service reports it, once,
   to its instance through `ICombatOutcomes.CreatureKilled`.
4. Characters in the hit are marked in combat.
5. On a death, the encounter hears of it and the death is broadcast.

`ICombatOutcomes` is World-side. `MapInstance` implements it, and nothing on the modding API can
report a kill.

---

## The Kill

`MapInstance`'s `ICombatOutcomes.CreatureKilled` runs on the tick thread. It ignores a creature that
is not in the instance. Otherwise, in order:

1. `creature.Script = null`. The corpse runs no AI and takes no more damage.
2. The locomotion stops the creature, then unregisters it, so the corpse comes to rest and blocks
   nothing.
3. Melee slots are released both ways: the slot the creature held, and the ring on it.
4. The corpse remover schedules its removal.
5. Loot drops, whoever the killer is.
6. If the killer is a character, it gains experience and may level up.

### Loot

`ILootRoller` rolls the template's `LootTableId` and its `MinGold`/`MaxGold` range (copper).
`ILootAllocator` picks an owner. The only implementation, `InstanceOwnerLootAllocator`, reserves
every drop for the instance's owner for `GameConfiguration.LootGracePeriod` (default 30 s). In an
instance with no owner, such as a town, every drop is free for all at once. The drops are placed
around the corpse, kept in the instance's `GroundLootStore`, and broadcast with `SLootSpawnedPacket`.
A loot failure is logged, and the kill still counts.

CLAUDE.md's World Simulation section describes loot tables and pickup.

### Experience and level-up

The award is the creature's `Experience`, scaled by the map's level band:

- A map with no band (`MapTemplate.MinLevel` or `MaxLevel` unset) scales by 1.
- Otherwise the award is multiplied by `ExperienceBandDecay` (default 0.75) once for each level the
  character sits outside the band. Inside the band it is not scaled.

If the character's experience plus the award reaches the requirement for its level
(`CharacterLevelExperiences`), the character gains one level and keeps the overflow. Its stats are
then recalculated: a living killer is refilled to the new maximums, and a dead one keeps its share of
each pool, so it is not revived. A level with no requirement row logs a warning and awards nothing.

---

## Corpse Removal

`CreatureCorpseRemover` (`ICorpseRemover`, `Avalon.World/Entities`) removes a corpse from its
instance once the template's `BodyRemoveTimer` has passed (`BodyRemoveTimerSecs`, default 10 s). It
runs at the start of each instance tick, and an instance ticks only while a character is in it.

---

## No Respawn

Creatures do not respawn. The time-based respawn scheduler was removed on purpose: the design is for
creatures to come back only through a deliberate mechanic, and none exists yet. A dead creature is
not revived either: `ICombatService.RevivePlayer` does nothing for a creature.

---

## Template Fields the World Server Does Not Read

These `CreatureTemplate` fields are deliberately unread. Do not assume any of them works.

| Field | Why |
|---|---|
| `RespawnTimerSecs` | Creatures do not respawn. Kept for a future revival mechanic. |
| `ArmorModifier` | `CombatService` has no mitigation step. |
| `ManaModifier` | Creatures cannot cast. |
| `RegenHealth` | Creatures do not regenerate health. |
| `BaseAttackTime` | The attack cadence is `CreatureCombatScript`'s fixed 2.25 s. |

`RangeAttackTime`, `DmgSchool`, `AIName`, `MovementType`, `MovementId` and `Family` are not read by
the world server either.

---

## Creature Casting

Creatures only attack in melee. Creature casting on the aimed-skill pipeline is #163; see
[spell-system.md](spell-system.md).

---

## Tests

| Area | Test class |
|---|---|
| Stat derivation | `CreatureStatDeriverShould` |
| Placement | `CreaturePlacementServiceShould` |
| Script construction | `AiScriptConstructibilityShould` |
| Kill reporting | `CombatServiceShould` |
| Experience and level-up | `ExperienceAwardShould`, `LevelUpStatsShould` |
| Loot on a kill | `MapInstanceLootShould` |
| `OnCharacterLeft` | `InstanceBroadcastIsolationShould` |
| Corpse removal | `CreatureCorpseRemoverShould` |
| Locomotion order | `MapInstanceLocomotionShould` |
| Velocity units | `LocomotionVelocityUnitsShould` |
| Melee slots | `MeleeSlotsShould` |
