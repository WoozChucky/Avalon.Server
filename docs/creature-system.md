# Creature System

This document describes the creature lifecycle: spawning, AI scripting, combat, experience rewards, respawn, and future spell support.

Related TODOs: [TODO-019](../TODO.md#todo-019), [TODO-021](../TODO.md#todo-021), [TODO-022](../TODO.md#todo-022), [TODO-023](../TODO.md#todo-023)

---

## Data Model

### `ICreatureMetadata` (current + planned)

Located in `src/Server/Avalon.World.Public/Creatures/ICreatureMetadata.cs`.

| Field              | Type           | Status    | Description                                     |
|--------------------|----------------|-----------|-------------------------------------------------|
| `Id`               | `CreatureTemplateId` | ✅ Present | Template identifier                           |
| `SpeedWalk`        | `float`        | ✅ Present | Walk speed in units/s                           |
| `SpeedRun`         | `float`        | ✅ Present | Run speed in units/s                            |
| `SpeedSwim`        | `float`        | ✅ Present | Swim speed in units/s                           |
| `StartPosition`    | `Vector3`      | ✅ Present | Initial spawn position                          |
| `Experience`       | `uint`         | 🔴 TODO-021 | XP awarded to killer                           |
| `RespawnTimer`     | `TimeSpan`     | 🔴 TODO-021 | Delay before creature re-spawns                |
| `BodyRemoveTimer`  | `TimeSpan`     | 🔴 TODO-021 | Delay before corpse is removed from the map    |
| `SpellIds`         | `IReadOnlyList<SpellId>` | 🔴 TODO-019 | Spells the creature can cast              |

### Default Values

When a creature template does not specify a value (or the field is newly added), defaults must match the previously hardcoded behaviour:

| Field            | Default       |
|------------------|---------------|
| `Experience`     | `20`          |
| `RespawnTimer`   | `3 minutes`   |
| `BodyRemoveTimer`| `2 minutes`   |
| `SpellIds`       | Empty list    |

---

## Creature Lifecycle

```
World startup
  └── CreatureSpawner.SpawnStartingEntities()
        ├── Load creature templates from WorldDb
        └── For each spawn point in map metadata:
              └── PoolManager.SpawnEntity(chunk, template)
                    ├── Create CreatureEntity with metadata
                    ├── Assign AiScript (resolved by ScriptName)
                    └── Chunk.AddCreature(creature)

Chunk.Update (per-tick, when Enabled)
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
  ├── Has spell available and off cooldown? (TODO-019)
  │   YES → chunk.QueueSpell(Creature, target, spell)
  │   NO  → Creature.SendAttackAnimation(null)  [melee]
  │         target.TakeDamage(Creature, Creature.Metadata.AttackDamage)
  └── Update last attack time
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

## Death and Respawn (TODO-021, TODO-022, TODO-023)

### `Chunk.OnCreatureKilled`

```
1. creature.Script = null              [verify thread safety — TODO-022]
2. creatureRespawner.ScheduleRespawn(creature)
3. Award XP to killer:
   killer.Experience += creature.Metadata.Experience   [TODO-022]
   Check level-up threshold
4. (Future) Spawn loot
```

### `CreatureRespawner.ScheduleRespawn`

```
respawnTimer interval = creature.Metadata.RespawnTimer    [TODO-023]
removeTimer interval  = creature.Metadata.BodyRemoveTimer [TODO-023]

removeTimer fires → chunk.RemoveCreature(creature)   // Remove corpse
respawnTimer fires → chunk.RespawnCreature(creature) // Re-add to pool
```

### Thread Safety for `creature.Script = null`

The Chunk update loop iterates creatures and calls `creature.Script?.Update(deltaTime)`. The question is whether `OnCreatureKilled` can be called from a different thread. 

**Audit result:** `OnCreatureKilled` is invoked from within `CreatureCombatScript.OnHit`, which is called from `Chunk.BroadcastUnitHit`, which is called by `CharacterAttackHandler`. The `CharacterAttackHandler` executes within the connection's `Update` call, which is part of the main `WorldServer.Update` loop — single-threaded. Therefore, `creature.Script = null` is safe.

Add an explicit comment to `Chunk.cs` confirming this analysis:
```csharp
// Safe: OnCreatureKilled is invoked on the main world update thread.
// Creature.Script is only read in the same update pass (single-threaded loop).
creature.Script = null;
```

---

## XP and Level-Up Flow

```
OnCreatureKilled(creature, killer)
  ├── killer is ICharacter?
  │   YES
  │   ├── expRequirement = world.Data.CharacterLevelExperiences[character.Level]
  │   ├── gain = creature.Metadata.Experience                  [TODO-022]
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

## Creature Spell Support (TODO-019)

See [spell-system.md — Creature Spell Support](spell-system.md#creature-spell-support-todo-019) for the full design.

Summary of `ICreatureMetadata` changes:

```csharp
// Add to ICreatureMetadata:
IReadOnlyList<SpellId> SpellIds { get; set; }
```

`CreatureCombatScript` uses these to initialise a list of `ISpell` instances at script start, tracking cooldowns per-spell-instance on the creature.

---

## Populated Data (World Database)

Creature templates are stored in the `creature_templates` table. The following columns need to be added via migration:

| Column             | SQL Type              | Default   |
|--------------------|-----------------------|-----------|
| `experience`       | `BIGINT NOT NULL`     | `20`      |
| `respawn_timer_ms` | `BIGINT NOT NULL`     | `180000`  |
| `remove_timer_ms`  | `BIGINT NOT NULL`     | `120000`  |

Migration class: `AddCreatureTemplateLootAndTimers` in `Avalon.Database.World`.

---

## Test Strategy

| Scenario                                                   | TODO |
|------------------------------------------------------------|------|
| `ICreatureMetadata` has all three new fields               | 021  |
| World data loader populates new fields from DB             | 021  |
| Killing creature with `Experience = 150` grants 150 XP    | 022  |
| Level-up triggers when XP exceeds threshold               | 022  |
| Thread-safety comment confirms single-threaded path       | 022  |
| Respawn fires at `creature.Metadata.RespawnTimer`         | 023  |
| Body removed at `creature.Metadata.BodyRemoveTimer`       | 023  |
| Creature with a spell → `QueueSpell` called on attack     | 019  |
| Creature without spell → melee attack taken               | 019  |
