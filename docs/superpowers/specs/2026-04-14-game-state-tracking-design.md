# Game State Tracking Redesign

**Date:** 2026-04-14
**Status:** Approved
**Scope:** `Avalon.World` — entity dirty flag system, `EntityTrackingSystem` simplification, `MapInstance` frame snapshot, secondary GC reductions

---

## Problem

The current state tracking system performs a **full snapshot comparison every tick** for every entity visible to every client. At the target scale of 250 concurrent map instances, each with up to 200 creatures and ~2 clients, this yields approximately 60 million field comparisons per second — the vast majority of which produce no change (idle entities between AI updates). This is a scalability concern before it becomes a runtime crisis, and the codebase is still small enough to address cleanly.

Secondary symptom: rising GC pressure from snapshot object allocations and `byte[] Fields` heap allocations per changed entity per broadcast.

---

## Goals

- Eliminate per-tick field comparison cost for unchanged entities
- Keep the existing `GameEntityFields` bitmask protocol and packet structure intact
- Lay groundwork for a future change-event pipeline (Option B) without requiring it now
- Reduce GC allocation volume from the tracking system

---

## Non-Goals

- Visibility/distance culling (not needed at current per-instance entity counts)
- Restructuring the `BroadcastStateTo` serialisation pipeline (phase 2)
- Adding cross-system event consumers (deferred to when concrete need arises)

---

## Architecture

The redesign has three layers that work together:

```
Entity property setter
  └─ sets _dirtyFields bitmask

MapInstance.Update() — step 5a (new)
  └─ ConsumeDirtyFields() on each entity
  └─ builds _frameDirtyFields: Dictionary<ObjectGuid, GameEntityFields>
       (only contains entities that actually changed this tick)

EntityTrackingSystem.Update() — receives _frameDirtyFields
  └─ known entity + absent from dirty map → skip entirely
  └─ known entity + in dirty map → fire EntityUpdated with bitmask
  └─ unknown entity → fire EntityAdded with GameEntityFields.All (enter-visibility)
  └─ was tracked, no longer present → fire EntityRemoved (exit-visibility)
```

---

## Section 1 — Entity Mutation Layer

Each mutable entity type (`CharacterEntity`, `Creature`, `WorldObject`) gains:

```csharp
private GameEntityFields _dirtyFields;

internal GameEntityFields ConsumeDirtyFields()
{
    var dirty = _dirtyFields;
    _dirtyFields = GameEntityFields.None;
    return dirty;
}
```

All properties that correspond to a tracked `GameEntityFields` member switch to a backing-field pattern:

```csharp
private float _currentHealth;
public float CurrentHealth
{
    get => _currentHealth;
    set { _currentHealth = value; _dirtyFields |= GameEntityFields.CurrentHealth; }
}
```

Rules:
- The backing field must never be assigned directly outside the property body
- `ConsumeDirtyFields()` is the only place `_dirtyFields` is cleared
- `ConsumeDirtyFields()` is called by `MapInstance`, not by the entity itself

---

## Section 2 — Frame-Level Dirty Snapshot in MapInstance

`MapInstance` gains a single pre-allocated frame dictionary:

```csharp
private readonly Dictionary<ObjectGuid, GameEntityFields> _frameDirtyFields = new(capacity: 256);
```

Step 5 of `MapInstance.Update()` is split:

```csharp
// Step 5a: collect dirty fields for this frame (runs once, before any client broadcast)
_frameDirtyFields.Clear();

foreach (var creature in _creatures)
{
    var dirty = creature.ConsumeDirtyFields();
    if (dirty != GameEntityFields.None)
        _frameDirtyFields[creature.Guid] = dirty;
}

foreach (var character in _characters)
{
    var dirty = character.ConsumeDirtyFields();
    if (dirty != GameEntityFields.None)
        _frameDirtyFields[character.Guid] = dirty;
}

foreach (var obj in objectSpells)
{
    var dirty = obj.ConsumeDirtyFields();
    if (dirty != GameEntityFields.None)
        _frameDirtyFields[obj.Guid] = dirty;
}

// Step 5b: update visibility state per client (unchanged call signature)
foreach (var character in _characters)
    character.CharacterGameState.Update(_creatures, _characters, objectSpells, _frameDirtyFields);
```

Only entities that changed this tick appear in `_frameDirtyFields`. For a map with 200 creatures where 190 are idle, the dictionary holds ~10 entries.

---

## Section 3 — EntityTrackingSystem Simplification

### Removed
- `_objects: Dictionary<ObjectGuid, CachedSnapshot>` → replaced by `_trackedGuids: HashSet<ObjectGuid>`
- `createObjectHandler`, `updateObjectHandler`, `changedFieldsHandler` constructor delegates
- `GetUnitChangedFields`, `GetWorldObjectChangedFields` comparison functions
- All snapshot copy types/methods

### Updated constructor signature
```csharp
public EntityTrackingSystem(int capacity)
```

### Updated Update logic

```csharp
public void Update(
    IReadOnlyList<IWorldObject> currentEntities,
    IReadOnlyDictionary<ObjectGuid, GameEntityFields> frameDirtyFields)
{
    _seenThisFrame.Clear();

    foreach (var entity in currentEntities)
    {
        _seenThisFrame.Add(entity.Guid);

        if (!_trackedGuids.Contains(entity.Guid))
        {
            // Enter-visibility: send full state regardless of dirty map
            _trackedGuids.Add(entity.Guid);
            EntityAdded?.Invoke(entity.Guid);
            continue;
        }

        // Known entity — only notify if something changed
        if (frameDirtyFields.TryGetValue(entity.Guid, out var dirtyFields))
        {
            EntityUpdated?.Invoke(entity.Guid, dirtyFields);
        }
        // else: entity unchanged this tick, skip entirely
    }

    // Exit-visibility: entities that were tracked but are no longer present
    foreach (var guid in _trackedGuids)
    {
        if (!_seenThisFrame.Contains(guid))
            _pendingRemovals.Add(guid);
    }

    foreach (var guid in _pendingRemovals)
    {
        _trackedGuids.Remove(guid);
        EntityRemoved?.Invoke(guid);
    }

    _pendingRemovals.Clear();
}
```

`_seenThisFrame` and `_pendingRemovals` are pre-allocated `HashSet<ObjectGuid>` / `List<ObjectGuid>` on the tracking system instance.

---

## Section 4 — GC Pressure Improvements

### Immediate (part of this change)
- `_objects` snapshot dictionary eliminated — no more snapshot object allocation on entity enter-visibility
- `UpdatedObjects` entries drop proportionally to the dirty map size — fewer `byte[] Fields` allocations per broadcast
- `NewObjects`, `UpdatedObjects`, `RemovedObjects` in `CharacterGameState` changed from `ISet<>` to pre-allocated `List<>` with initial capacity 8 — list clears are cheaper than HashSet clears for append-and-iterate usage

### Phase 2 (separate work)
The remaining GC source is `byte[] Fields` in `ObjectAdd` / `ObjectUpdate` — one heap allocation per changed entity per broadcast. The fix is to write entity fields directly into the outgoing connection buffer in a single pass, eliminating the intermediate byte array. This requires restructuring `BroadcastStateTo` and `WorldObjectWriter` and is deferred until the dirty flag system is stable.

---

## Section 5 — Testing

All tests are pure unit tests (xUnit, no infrastructure). Test files follow existing conventions (`<Subject>Should.cs`, `Should_<verb>_<condition>` method names).

### `EntityDirtyFlagShould.cs`
- `Should_mark_CurrentHealth_dirty_when_property_is_set`
- `Should_mark_Position_dirty_when_property_is_set`
- *(one per tracked field per entity type)*
- `Should_accumulate_multiple_dirty_fields_before_consume`
- `Should_clear_dirty_fields_after_ConsumeDirtyFields`
- `Should_return_None_on_second_ConsumeDirtyFields_without_mutation`

### `EntityTrackingSystemShould.cs`
- `Should_fire_EntityAdded_when_entity_is_new`
- `Should_fire_EntityUpdated_with_correct_fields_when_entity_is_in_dirty_map`
- `Should_not_fire_EntityUpdated_when_entity_is_absent_from_dirty_map`
- `Should_fire_EntityRemoved_when_entity_leaves_the_set`
- `Should_not_fire_EntityRemoved_when_entity_was_never_tracked`
- `Should_fire_EntityAdded_again_if_entity_re_enters_after_removal`

### `CharacterGameStateShould.cs`
- `Should_populate_NewObjects_for_first_seen_entities`
- `Should_populate_UpdatedObjects_only_for_entities_in_dirty_map`
- `Should_produce_empty_UpdatedObjects_when_dirty_map_is_empty` *(regression guard for idle-entity invariant)*
- `Should_populate_RemovedObjects_for_entities_no_longer_present`

---

## Future: Option B (Change-Event Pipeline)

The dirty flag design intentionally keeps entity mutation internal. When a concrete cross-system observer need arises (AI reactions, telemetry, persistence hooks), the path to Option B is:

1. Add `event Action<GameEntityFields>? StateChanged` to entities
2. Fire it alongside setting `_dirtyFields` in each setter
3. Per-instance subscribers accumulate into the same `_frameDirtyFields` dictionary (or a parallel log)

The tracking system does not need to change — it already reads from the dirty map. The event layer becomes purely additive.
