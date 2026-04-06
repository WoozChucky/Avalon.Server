# Instanced Map System — Design & Refactor Plan

> Status: **Draft** — for review and refinement before implementation.

---

## Context

The current world server uses a WoW-style persistent open world split into spatial `Chunk` units for selective ticking. This document plans a pivot to a **Path of Exile-style instanced map** system.

### Goals

- **Town maps** — shared hubs with a player cap (default 30). Multiple instances of the same town are created automatically when all existing ones are full. New players are always auto-routed to the least-populated instance that still has room.
- **Normal maps** — private instanced areas, one per player (group-ready by design). A 15-minute expiry countdown starts when the last player leaves. Re-entering within that window returns the player to the same live instance with its existing creature/loot state. After expiry the instance is freed.
- Players move between maps via an explicit `CEnterMapPacket`; the server validates that the player is within range of a portal defined for that map pair.
- Logging out inside a normal map saves the character to the associated town, but the instance survives its remaining timer. The player can rejoin from town after logging back in.

### What Is NOT Changing

- The 60 Hz tick loop in `WorldServer`
- `ChunkMetadata` as a **static data container** (creature spawn definitions, mesh file paths are still read from the binary map files)
- Navmesh generation (`NavigationMeshBaker`) and per-area navigation (`ChunkNavigator`)
- `CharacterEntity`, `ICreature`, `CreatureRespawner` (logic unchanged, just re-scoped)
- `ChunkSpellSystem` / `ISpellQueueSystem` (re-scoped to instance)
- `EntityTrackingSystem` and `CharacterCharacterGameState` (unchanged)
- The packet broadcast logic from `Chunk.BroadcastChunkStateTo` (ported verbatim)

---

## Locked Design Decisions

| Question | Decision |
|---|---|
| Chunk as simulation unit | **Dropped.** Each `MapInstance` is a single flat tick context. |
| Normal map re-entry | **Same instance** for 15 min from last player leaving (PoE-style). |
| Map transition trigger | **Explicit `CEnterMapPacket`** + server-side proximity check to portal position. |
| Logout from normal map | Character saved to town; instance continues its 15-min timer. |
| Town overflow | Auto-route to least-full; create new instance only when all are full. |
| Group support | Solo only for now; ownership model stubs `List<long> AllowedAccounts`. |

---

## New Type Taxonomy

### MapType enum (replaces MapInstanceType)

**File to create:** `src/Shared/Avalon.Domain/World/Enums/MapType.cs`
**File to delete:** `src/Server/Avalon.World.Public/Enums/MapInstanceType.cs`

```csharp
public enum MapType
{
    Town   = 0,   // Shared hub; multiple instances with MaxPlayers cap
    Normal = 1,   // Private instanced map; 15-min expiry timer
}
```

---

## Data Model Changes

### MapTemplate (modified)

**File:** `src/Shared/Avalon.Domain/World/MapTemplate.cs`

| Change | Detail |
|---|---|
| **Add** `MapType MapType` | Replaces `MapInstanceType InstanceType` |
| **Add** `float DefaultSpawnX/Y/Z` | Where players appear when entering this map |
| **Add** `MapTemplateId? ReturnMapId` | Normal maps point to their home town; null for towns |
| **Remove** `MapInstanceType InstanceType` | WoW-era enum, no longer used |

### MapPortal (new entity)

**File to create:** `src/Shared/Avalon.Domain/World/MapPortal.cs`

```csharp
public class MapPortal
{
    public int Id { get; set; }
    public MapTemplateId SourceMapId { get; set; }   // Map containing the portal
    public MapTemplateId TargetMapId { get; set; }   // Destination map
    public float X { get; set; }                     // Portal centre in world space
    public float Y { get; set; }
    public float Z { get; set; }
    public float Radius { get; set; }                // Server-side proximity radius
}
```

**File to create:** `src/Server/Avalon.Database.World/Repositories/MapPortalRepository.cs`

Add `DbSet<MapPortal> MapPortals` to `WorldDbContext` and a corresponding `IMapPortalRepository`.

**EF migration name:** `AddInstancedMapSystem`
- Adds `MapPortal` table
- Adds `MapType`, `DefaultSpawnX`, `DefaultSpawnY`, `DefaultSpawnZ`, `ReturnMapId` columns to `MapTemplate`
- Removes `InstanceType` column from `MapTemplate`

---

## New Simulation Contract

### ISimulationContext (new interface)

**File to create:** `src/Server/Avalon.World.Public/Instances/ISimulationContext.cs`

Replaces `IChunk` as the contract used by creature AI scripts, the spell system, and `CreatureRespawner`. `MapInstance` implements this interface.

```csharp
public interface ISimulationContext
{
    IReadOnlyDictionary<ObjectGuid, ICharacter> Characters { get; }
    IReadOnlyDictionary<ObjectGuid, ICreature>  Creatures  { get; }

    bool QueueSpell(ICharacter caster, IUnit? target, ISpell spell);
    void RespawnCreature(ICreature creature);
    void RemoveCreature(ICreature creature);
    void BroadcastUnitHit(IUnit attacker, IUnit target, uint health, uint damage);
    void BroadcastUniStartCast(IUnit caster, float castTime);
}
```

> **Why not reuse `IChunk`?**  
> `IChunk` carries spatial concepts (`Id`, `Enabled`, `Metadata`, `Neighbors`) that are meaningless for flat instances. `ISimulationContext` is the minimal contract scripts actually need.

---

## New Instance Model

### IMapInstance

**File to create:** `src/Server/Avalon.World.Public/Instances/IMapInstance.cs`

```csharp
public interface IMapInstance : ISimulationContext
{
    Guid         InstanceId      { get; }
    MapTemplateId TemplateId     { get; }
    MapType       MapType        { get; }
    long?         OwnerAccountId { get; }              // null for Town instances
    IReadOnlyList<long> AllowedAccounts { get; }      // stub for future group support
    int           PlayerCount    { get; }
    DateTime?     LastEmptyAt    { get; }              // null while any player is inside

    bool IsExpired(TimeSpan expiry);
    bool CanAcceptPlayer(ushort maxPlayers);

    void AddCharacter(IWorldConnection connection);
    void RemoveCharacter(IWorldConnection connection);
    void SpawnStartingEntities(IPoolManager poolManager);
    void Update(TimeSpan deltaTime);
}
```

### MapInstance (new class)

**File to create:** `src/Server/Avalon.World/Instances/MapInstance.cs`

Absorbs all logic currently split across `Map` and `Chunk`. Key internals:

| Field | Description |
|---|---|
| `Dictionary<ObjectGuid, ICharacter> _characters` | Active players |
| `Dictionary<ObjectGuid, ICreature> _creatures` | Active creatures |
| `ISpellQueueSystem _spellSystem` | Scoped to this instance (was `ChunkSpellSystem`) |
| `ICreatureRespawner _creatureRespawner` | Receives `ISimulationContext = this` |
| `List<(ChunkMetadata metadata, IChunkNavigator navigator)> _navigators` | One navmesh per chunk data region |
| `float _lastBroadcastTime` | State broadcast gate (100 ms, unchanged) |

**`Update(TimeSpan deltaTime)`** — direct port of `Chunk.Update()`:

```
1. _creatureRespawner.Update(deltaTime)
2. foreach character → connection.Update(MapSessionFilter) + character.Update(deltaTime)
3. _spellSystem.Update(deltaTime, objectSpells)
4. foreach creature → creature.Script?.Update(deltaTime)
5. foreach character → CharacterGameState.Update(_creatures, _characters, objectSpells)
6. foreach character → BroadcastChunkStateTo(character)
7. Reset _lastBroadcastTime when >= BroadcastInterval
```

**`RemoveCharacter`** — same as before, plus: if `_characters.Count == 0` → set `LastEmptyAt = DateTime.UtcNow`.  
**`AddCharacter`** — same as before, plus: clear `LastEmptyAt = null`.

**Navigation routing:** `MapInstance.GetNavigatorForPosition(Vector3 pos)` iterates `_navigators` and returns the navigator whose `ChunkMetadata` bounds contain the position. Creature scripts receive the `MapInstance` (as `ISimulationContext`); the instance passes itself to pathfinding calls.

---

## Instance Registry

### IInstanceRegistry

**File to create:** `src/Server/Avalon.World.Public/Instances/IInstanceRegistry.cs`

```csharp
public interface IInstanceRegistry
{
    IReadOnlyCollection<IMapInstance> ActiveInstances { get; }

    /// <summary>Returns the least-full town instance. Creates a new one if all are at capacity.</summary>
    IMapInstance GetOrCreateTownInstance(MapTemplateId templateId, ushort maxPlayers);

    /// <summary>Returns the player's existing live instance if within expiry window; else creates a new one.</summary>
    IMapInstance GetOrCreateNormalInstance(long accountId, MapTemplateId templateId);

    IMapInstance? GetInstanceById(Guid instanceId);
    void ProcessExpiredInstances(TimeSpan normalMapExpiry);
}
```

### InstanceRegistry (new class)

**File to create:** `src/Server/Avalon.World/Instances/InstanceRegistry.cs`

Internal state:

```csharp
ConcurrentDictionary<Guid, MapInstance> _instances
// accountId → { mapTemplateId → instanceId } — for Normal map re-entry
ConcurrentDictionary<long, Dictionary<MapTemplateId, Guid>> _accountInstanceMap
```

**`GetOrCreateTownInstance`:**
1. Filter `_instances` by `TemplateId` and `MapType == Town`
2. Pick the one with the lowest `PlayerCount` that passes `CanAcceptPlayer(maxPlayers)`
3. If none found (all full or none exist): create a new `MapInstance`, call `SpawnStartingEntities` + initialise navigators, register it

**`GetOrCreateNormalInstance`:**
1. Check `_accountInstanceMap[accountId][templateId]` — if the Guid maps to an existing, non-expired instance: return it
2. Otherwise: create a new `MapInstance` (OwnerAccountId = accountId, AllowedAccounts = [accountId]), register in both `_instances` and `_accountInstanceMap`

**`ProcessExpiredInstances(expiry)`:**
- Iterate instances where `MapType == Normal` and `IsExpired(expiry)` and `PlayerCount == 0`
- Remove from `_instances` and `_accountInstanceMap`
- Log: `"Normal map instance {InstanceId} for map {TemplateId} freed after expiry"`

---

## New Network Protocol

### CEnterMapPacket

**File to create:** `src/Shared/Avalon.Network.Packets/World/CEnterMapPacket.cs`  
**New `NetworkPacketType` value:** `CMSG_ENTER_MAP`

```csharp
public class CEnterMapPacket : Packet
{
    public ushort TargetMapId { get; set; }
}
```

### SMapTransitionPacket

**File to create:** `src/Shared/Avalon.Network.Packets/World/SMapTransitionPacket.cs`  
**New `NetworkPacketType` value:** `SMSG_MAP_TRANSITION`

```csharp
public enum MapTransitionResult : byte
{
    Success       = 0,
    MapNotFound   = 1,
    NotNearPortal = 2,
    LevelTooLow   = 3,
    LevelTooHigh  = 4,
}

public class SMapTransitionPacket : Packet
{
    public MapTransitionResult Result      { get; set; }
    public Guid                InstanceId  { get; set; }
    public ushort              MapId       { get; set; }
    public float               SpawnX      { get; set; }
    public float               SpawnY      { get; set; }
    public float               SpawnZ      { get; set; }
    public string              MapName     { get; set; }
    public string              MapDescription { get; set; }
}
```

---

## New Handler: EnterMapHandler

**File to create:** `src/Server/Avalon.Server.World/Handlers/EnterMapHandler.cs`

```
[PacketHandler(NetworkPacketType.CMSG_ENTER_MAP)]

ExecuteAsync flow:
1.  Guard: connection.InGame — else ignore (packet arrives before character selection)
2.  Resolve current instance from world.InstanceRegistry.GetInstanceById(connection.Character.InstanceIdGuid)
3.  Load MapTemplate for packet.TargetMapId
4.  Load portals for current map — find one where TargetMapId == packet.TargetMapId
5.  If no portal → send SMapTransitionPacket(MapNotFound) and return
6.  Proximity check: Vector3.Distance(character.Position, portal.Position) <= portal.Radius
       → if too far: send SMapTransitionPacket(NotNearPortal) and return
7.  Level check: targetTemplate.MinLevel / MaxLevel
       → send LevelTooLow / LevelTooHigh if out of range
8.  Resolve target instance:
       Town  → instanceRegistry.GetOrCreateTownInstance(targetMapId, targetTemplate.MaxPlayers ?? 30)
       Normal → instanceRegistry.GetOrCreateNormalInstance(accountId, targetMapId)
9.  world.TransferPlayer(connection, targetInstance):
       a. currentInstance.RemoveCharacter(connection)
       b. character.Map      = targetMapId
       c. character.Position = new Vector3(targetTemplate.DefaultSpawnX/Y/Z)
       d. character.InstanceIdGuid = targetInstance.InstanceId
       e. targetInstance.AddCharacter(connection)
10. Send SMapTransitionPacket(Success, instanceId, mapId, spawnPosition, name, desc)
11. Enqueue DB update for character.Map + position via connection.AddQueryCallback
```

---

## IWorld & World Changes

### IWorld interface

**File:** `src/Server/Avalon.World.Public/IWorld.cs` (currently co-located in `World.cs`)

```csharp
public interface IWorld
{
    WorldId           Id              { get; }
    string            MinVersion      { get; }
    string            CurrentVersion  { get; }
    GameConfiguration Configuration   { get; }
    IInstanceRegistry InstanceRegistry { get; }   // replaces WorldGrid Grid
    StaticData        Data            { get; }

    void SpawnInInstance(IWorldConnection connection, IMapInstance instance);
    void TransferPlayer(IWorldConnection connection, IMapInstance targetInstance);
    Task DeSpawnPlayerAsync(IWorldConnection connection);
}
```

### World.LoadAsync

```
1. Load world metadata and static data (unchanged)
2. await _mapManager.LoadAsync()  — now loads all templates AND portal definitions
3. Construct InstanceRegistry
4. For each Town MapTemplate:
   a. Load VirtualizedMap binary via mapManager.LoadMapDataAsync(template)
   b. Create MapInstance (MapType.Town)
   c. Initialise navigators (one IChunkNavigator per ChunkMetadata.MeshFile)
   d. SpawnStartingEntities(_poolManager)
   e. Register in InstanceRegistry
5. Normal map instances are NOT pre-created — spawned on demand by EnterMapHandler
```

### World.Update

```csharp
public void Update(TimeSpan deltaTime)
{
    GameTime.UpdateGameTimers(deltaTime);
    // Hot-reload timer logic (unchanged)

    foreach (IMapInstance instance in InstanceRegistry.ActiveInstances)
        instance.Update(deltaTime);

    InstanceRegistry.ProcessExpiredInstances(TimeSpan.FromMinutes(15));
}
```

### World.DeSpawnPlayerAsync — Normal Map Logout

After removing the player from the instance, redirect character to town if they were in a Normal map:

```csharp
IMapInstance? instance = InstanceRegistry.GetInstanceById(connection.Character.InstanceIdGuid);
if (instance?.MapType == MapType.Normal)
{
    MapTemplate? template = _mapManager.Templates.FirstOrDefault(t => t.Id == instance.TemplateId);
    if (template?.ReturnMapId is { } returnMapId)
    {
        MapTemplate? town = _mapManager.Templates.FirstOrDefault(t => t.Id == returnMapId);
        dbCharacter.Map = returnMapId.Value;
        dbCharacter.X   = town?.DefaultSpawnX ?? 0f;
        dbCharacter.Y   = town?.DefaultSpawnY ?? 0f;
        dbCharacter.Z   = town?.DefaultSpawnZ ?? 0f;
    }
}
// Continue with existing save logic (Online = false, timing stats, etc.)
```

The instance is **not** freed on logout — its `LastEmptyAt` timer governs cleanup independently.

---

## ICharacter Changes

**File:** `src/Server/Avalon.World.Public/Characters/ICharacter.cs`

- **Remove** `ChunkId ChunkId { get; set; }` — instances are flat; no spatial sub-unit
- **Keep** `MapId Map { get; set; }` — still the map template ID
- **Add** `Guid InstanceIdGuid { get; set; }` — the live instance the character is in (previously a random-Guid string placeholder on the DB entity; now a first-class runtime property; still serialised to `Character.InstanceId` as string for DB persistence)

---

## CharacterSelectHandler Changes

**File:** `src/Server/Avalon.World/Handlers/CharacterSelectHandler.cs`

In `OnCharacterReceived`:

```csharp
// Resolve the saved town map and place player in the least-full instance
MapTemplate townTemplate = world.Data.MapTemplates.First(t => t.Id == (MapTemplateId)character.Map);
IMapInstance instance = world.InstanceRegistry.GetOrCreateTownInstance(
    townTemplate.Id, townTemplate.MaxPlayers ?? 30);

connection.Character.InstanceIdGuid = instance.InstanceId;
world.SpawnInInstance(connection, instance);

// MapInfo sent to client uses real Guid, not Guid.NewGuid()
MapInfo mapInfo = new()
{
    MapId      = character.Map,
    InstanceId = instance.InstanceId,
    Name       = townTemplate.Description,
    Description = townTemplate.Description
};
```

The `// TODO: Implement when instances are a thing` comment and `Guid.NewGuid()` call are removed.

---

## CharacterMovementHandler Changes

**File:** `src/Server/Avalon.World/Handlers/CharacterMovementHandler.cs`

- **Remove** the call to `world.Grid.OnPlayerMoved(connection)` — there are no chunk borders to detect
- Portal proximity is **not** checked during movement; `EnterMapHandler` validates it when the client sends `CEnterMapPacket`

---

## PoolManager & CreatureRespawner Changes

### IPoolManager interface

**File:** `src/Server/Avalon.World/Pools/PoolManager.cs`

```csharp
// Before
public interface IPoolManager
{
    void SpawnStartingEntities(Chunk chunk);
    void SpawnEntity(Chunk chunk, ICreature creature);
}

// After
public interface IPoolManager
{
    void SpawnStartingEntities(ISimulationContext context, IReadOnlyList<ChunkMetadata> chunkData);
    void SpawnEntity(ISimulationContext context, IReadOnlyList<ChunkMetadata> chunkData, ICreature creature);
}
```

### CreatureRespawner constructor

**File:** `src/Server/Avalon.World/Entities/CreatureRespawner.cs`

```csharp
// Before
public class CreatureRespawner(IChunk chunk)

// After
public class CreatureRespawner(ISimulationContext context)
```

All `chunk.*` calls become `context.*`.

---

## AiScript Constructor Change

**File:** `src/Server/Avalon.World.Public/Scripts/AiScript.cs`

```csharp
// Before
protected AiScript(ICreature creature, IChunk chunk)

// After
protected AiScript(ICreature creature, ISimulationContext context)
```

All concrete scripts in `src/Server/Avalon.World/Scripts/Creatures/` and `src/Server/Avalon.World/Scripts/Spells/` update their constructors. `PoolManager` passes the `MapInstance` (which implements `ISimulationContext`) directly.

---

## IAvalonMapManager Changes

**File:** `src/Server/Avalon.World/Maps/MapManager.cs`

| Change | Detail |
|---|---|
| Remove `EnumerateOpenWorldAsync` | No longer needed — startup only loads towns; normals are on-demand |
| Add `IReadOnlyList<MapTemplate> Templates { get; }` | All templates, not just OpenWorld |
| Add `IReadOnlyList<MapPortal> Portals { get; }` | Consumed by `EnterMapHandler` |
| Add `Task<VirtualizedMap> LoadMapDataAsync(MapTemplate)` | Used both at startup (towns) and on demand (normals) |

---

## ServiceExtensions Changes

**File:** `src/Server/Avalon.Server.World/Extensions/ServiceExtensions.cs`

- **Add:** `services.AddSingleton<IInstanceRegistry, InstanceRegistry>()`
- **Add:** portal repository registration (via the `AddWorldDatabase()` extension or directly)
- No other structural DI changes required

---

## File Inventory

### Files to Create

| Path | Purpose |
|---|---|
| `src/Shared/Avalon.Domain/World/Enums/MapType.cs` | New MapType enum |
| `src/Shared/Avalon.Domain/World/MapPortal.cs` | Portal domain entity |
| `src/Server/Avalon.World.Public/Instances/ISimulationContext.cs` | Minimal context contract for scripts |
| `src/Server/Avalon.World.Public/Instances/IMapInstance.cs` | Public instance interface |
| `src/Server/Avalon.World.Public/Instances/IInstanceRegistry.cs` | Public registry interface |
| `src/Server/Avalon.World/Instances/MapInstance.cs` | Core instance class (absorbs Map + Chunk) |
| `src/Server/Avalon.World/Instances/InstanceRegistry.cs` | Active instance tracker + expiry |
| `src/Server/Avalon.Database.World/Repositories/MapPortalRepository.cs` | EF repo for MapPortal |
| `src/Shared/Avalon.Network.Packets/World/CEnterMapPacket.cs` | Client → Server map enter |
| `src/Shared/Avalon.Network.Packets/World/SMapTransitionPacket.cs` | Server → Client transition result |
| `src/Server/Avalon.Server.World/Handlers/EnterMapHandler.cs` | Handles CEnterMapPacket |
| EF migration `AddInstancedMapSystem` | DB schema update |

### Files to Delete

| Path | Reason |
|---|---|
| `src/Server/Avalon.World/Maps/WorldGrid.cs` | Replaced by InstanceRegistry |
| `src/Server/Avalon.World/Maps/Map.cs` | Logic absorbed into MapInstance |
| `src/Server/Avalon.World/Maps/Chunk.cs` | Logic absorbed into MapInstance |
| `src/Server/Avalon.World.Public/Enums/MapInstanceType.cs` | Replaced by MapType |

`IChunk` interface in `IChunk.cs` is deleted. The data types it co-locates (`ChunkMetadata`, `CreatureInfo`, `TreeInfo`, `NavMeshInfo`) move to a new file `src/Server/Avalon.World.Public/Maps/MapData.cs`.

---

## Implementation Sequence

### Phase 1 — Domain & Database (no functional change)
1. Create `MapType` enum
2. Update `MapTemplate`: add new columns, remove `InstanceType`
3. Create `MapPortal` entity + `IMapPortalRepository` + `MapPortalRepository`
4. Update `WorldDbContext`: add `DbSet<MapPortal>`, reconfigure `MapTemplate`, update seed data
5. Add EF migration `AddInstancedMapSystem`
6. Update `IAvalonMapManager` to load portals + all templates

### Phase 2 — New Abstractions (no functional change)
7. Create `ISimulationContext`
8. Create `IMapInstance`, `IInstanceRegistry`
9. Update `AiScript` base ctor: `IChunk` → `ISimulationContext`
10. Update all concrete AI and spell scripts

### Phase 3 — Instance Core
11. Implement `MapInstance` (port Chunk.Update, Chunk.BroadcastChunkStateTo, AddCharacter, RemoveCharacter)
12. Update `CreatureRespawner`: `IChunk` → `ISimulationContext`
13. Update `PoolManager`: `Chunk` → `ISimulationContext` + `IReadOnlyList<ChunkMetadata>`
14. Implement `InstanceRegistry` (town routing, normal re-entry, expiry)

### Phase 4 — World Wiring
15. Update `IWorld` interface
16. Rewrite `World.LoadAsync` (town instances at startup only)
17. Rewrite `World.Update` (tick instances, process expired)
18. Implement `SpawnInInstance`, `TransferPlayer`, update `DeSpawnPlayerAsync`

### Phase 5 — Handlers & Packets
19. Add `CMSG_ENTER_MAP` and `SMSG_MAP_TRANSITION` to `NetworkPacketType` enum
20. Create `CEnterMapPacket` and `SMapTransitionPacket`
21. Create `EnterMapHandler`
22. Update `CharacterSelectHandler` (town instance routing, real InstanceId)
23. Update `CharacterMovementHandler` (remove `OnPlayerMoved` call)
24. Register `IInstanceRegistry` in `ServiceExtensions`

### Phase 6 — Cleanup
25. Delete `WorldGrid.cs`, `Map.cs`, `Chunk.cs`, `MapInstanceType.cs`
26. Move data types to `MapData.cs`; delete `IChunk` interface
27. Remove `ICharacter.ChunkId` and all references
28. Update unit tests (Chunk mocks → MapInstance / ISimulationContext mocks)
29. Update `CLAUDE.md`

---

## Verification Checklist

| Scenario | Expected Result |
|---|---|
| Server startup | At least one `MapInstance` (Town) created per town template; logged at `Information` level |
| Character select | Player spawns in correct town instance; `SCharacterSelectedPacket.MapInfo.InstanceId` is a real (non-random) Guid |
| Portal enter (town → normal) | `CEnterMapPacket` near a portal creates a new normal instance; client receives `SMapTransitionPacket(Success)` |
| Portal — too far | `NotNearPortal` result; no transfer |
| Normal map re-entry | Leaving and re-entering within 15 min returns the same `InstanceId` |
| 15-min expiry (unit test) | `ProcessExpiredInstances` frees the instance; next entry creates a fresh one |
| Town overflow (`MaxPlayers = 2`, 3 connections) | Two instances created; instances have 2 and 1 player respectively |
| Logout from normal map | `character.Map` saved as town map ID; character logs in at town on next session |
| Creature AI | Creatures patrol, attack, and respawn correctly inside `MapInstance` |
| Spell system | Spells resolve targets and broadcast animations within the instance |
