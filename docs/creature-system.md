# Creature System

This document describes the creature lifecycle: spawning, AI scripting, combat, experience rewards, and respawn.

---

## Data Model

### `ICreatureMetadata`

Located in `src/Server/Avalon.World.Public/Creatures/ICreatureMetadata.cs`.

| Field              | Type           | Description                                     |
|--------------------|----------------|-------------------------------------------------|
| `Id`               | `CreatureTemplateId` | Template identifier                       |
| `SpeedWalk`        | `float`        | Walk speed in units/s                           |
| `SpeedRun`         | `float`        | Run speed in units/s                            |
| `SpeedSwim`        | `float`        | Swim speed in units/s                           |
| `StartPosition`    | `Vector3`      | Initial spawn position                          |
| `Experience`       | `uint`         | XP awarded to killer                            |
| `RespawnTimer`     | `TimeSpan`     | Delay before creature re-spawns                 |
| `BodyRemoveTimer`  | `TimeSpan`     | Delay before corpse is removed from the map     |

### Default Values

| Field            | Default       |
|------------------|---------------|
| `Experience`     | `20`          |
| `RespawnTimer`   | `3 minutes`   |
| `BodyRemoveTimer`| `2 minutes`   |

---

## Creature Lifecycle

```
World startup
  └── CreatureSpawner.SpawnStartingEntities()
        ├── Load creature templates from WorldDb
        └── For each spawn point in map metadata:
              └── PoolManager.SpawnEntity(context, chunkData)
                    ├── Create CreatureEntity with metadata
                    ├── Assign AiScript (resolved by ScriptName)
                    └── context.AddCreature(creature)

MapInstance.Update (per-tick, when Enabled)
  └── For each creature:
        └── creature.Script?.Update(deltaTime)
              ├── CreatureIdleScript     → patrol / wait
              ├── CreaturePatrolScript   → follow waypoints
              ├── CreatureCombatScript   → chase, attack, return
              └── CreatureRangeDetectorScript → trigger combat
```

---

## Combat Flow

### Entering Combat

`CreatureRangeDetectorScript` monitors player proximity. When a player enters detection range:

```
OnEnteredRange(character)
  └── CreatureCombatScript.State = CombatState.Combat
        └── _target = character
```

### Attack Loop

`CreatureCombatScript.AttackTarget(deltaTime)`:

```
Time since last attack > AttackInterval ?
  YES
  ├── Creature.SendAttackAnimation(null)  [melee]
  └── target.TakeDamage(Creature, Creature.Metadata.AttackDamage)
  Update last attack time
```

### Chase and Return

```
CombatState.Combat:
  ├── Target within AttackRange → attack
  └── Target outside AttackRange → recalculate path and chase

Distance from InitialPosition > MaxChaseDistance:
  └── CombatState.Returning → follow path back; regenerate health to full
```

---

## Death and Respawn

### `MapInstance.OnCreatureKilled`

```
1. creature.Script = null
   // Safe: OnCreatureKilled is invoked on the main world update thread.
   // Creature.Script is only read in the same update pass (single-threaded loop).
2. creatureRespawner.ScheduleRespawn(creature)
3. Award XP to killer:
   killer.Experience += creature.Metadata.Experience
   Check level-up threshold
4. (Future) Spawn loot
```

### `CreatureRespawner.ScheduleRespawn`

```
respawnTimer interval = creature.Metadata.RespawnTimer
removeTimer interval  = creature.Metadata.BodyRemoveTimer

removeTimer fires → context.RemoveCreature(creature)   // Remove corpse
respawnTimer fires → context.RespawnCreature(creature) // Re-add to pool
```

---

## XP and Level-Up Flow

```
OnCreatureKilled(creature, killer)
  ├── killer is ICharacter?
  │   YES
  │   ├── expRequirement = world.Data.CharacterLevelExperiences[character.Level]
  │   ├── gain = creature.Metadata.Experience
  │   ├── character.Experience + gain >= expRequirement.Experience?
  │   │   YES → Level up:
  │   │         character.Level++
  │   │         character.Experience = overflow XP
  │   │         character.RequiredExperience = next level threshold
  │   │         Send SLevelUpPacket to client
  │   └── NO  → character.Experience += gain
  └── Persist character XP (scheduled DB update)
```

> **Note:** `SLevelUpPacket` broadcast is not yet implemented. Add it as part of the XP/level-up flow.

---

## Creature Spell Support

Creatures currently always execute melee attacks. To support caster creatures, `ICreatureMetadata` needs a `IReadOnlyList<SpellId> SpellIds` property. `CreatureCombatScript` would use these to initialise per-spell cooldown tracking at script start, selecting a spell over melee when one is available and off cooldown.

See [spell-system.md — Creature Spell Support](spell-system.md#creature-spell-support) for the full design.

---

## Populated Data (World Database)

Creature templates are stored in the `creature_templates` table with the following columns:

| Column             | SQL Type              | Default   |
|--------------------|-----------------------|-----------|
| `experience`       | `BIGINT NOT NULL`     | `20`      |
| `respawn_timer_ms` | `BIGINT NOT NULL`     | `180000`  |
| `remove_timer_ms`  | `BIGINT NOT NULL`     | `120000`  |

---

## Test Coverage

| Scenario                                                   |
|------------------------------------------------------------|
| `ICreatureMetadata` exposes experience, respawn, and body-remove fields |
| Killing creature with `Experience = 150` grants 150 XP     |
| Level-up triggers when XP exceeds threshold               |
| Thread-safety: `OnCreatureKilled` runs on main update thread |
| Respawn fires at `creature.Metadata.RespawnTimer`         |
| Body removed at `creature.Metadata.BodyRemoveTimer`       |
| Creature without spell → melee attack                     |
