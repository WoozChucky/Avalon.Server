# Instanced Map System

This document describes the instanced map architecture used by the World server.

---

## Overview

Avalon uses a Path of Exile-style instanced map system rather than a single persistent open world.

### Map Types

| Type    | Description |
|---------|-------------|
| `Town`  | Shared hub with a player cap (default 30). Multiple instances are created automatically when all existing ones are full. New players are always routed to the least-populated instance that still has room. |
| `Normal`| Private instanced area, one per player (group-ready by design). A 15-minute expiry countdown starts when the last player leaves. Re-entering within that window returns the player to the same live instance. After expiry the instance is freed. |

Players move between maps via `CEnterMapPacket`; the server validates that the player is within range of a portal defined for that map pair.

Logging out inside a Normal map saves the character to the associated town. The instance survives its remaining timer — the player can rejoin from town after logging back in.

---

## Core Types

### `MapType` enum

```csharp
// src/Shared/Avalon.Domain/World/Enums/MapType.cs
public enum MapType
{
    Town   = 0,   // Shared hub; multiple instances with MaxPlayers cap
    Normal = 1,   // Private instanced map; 15-min expiry timer
}
```

### `ISimulationContext`

```csharp
// src/Server/Avalon.World.Public/Instances/ISimulationContext.cs
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

`ISimulationContext` is the minimal contract used by creature AI scripts, the spell system, and `CreatureRespawner`. `MapInstance` is the sole simulation unit — there is no sub-map spatial division.

### `IMapInstance`

```csharp
// src/Server/Avalon.World.Public/Instances/IMapInstance.cs
public interface IMapInstance : ISimulationContext
{
    Guid         InstanceId      { get; }
    MapTemplateId TemplateId     { get; }
    MapType       MapType        { get; }
    long?         OwnerAccountId { get; }              // null for Town instances
    IReadOnlyList<long> AllowedAccounts { get; }
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

### `IInstanceRegistry`

```csharp
// src/Server/Avalon.World.Public/Instances/IInstanceRegistry.cs
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

---

## MapInstance

`MapInstance` (`src/Server/Avalon.World/Instances/MapInstance.cs`) is the core simulation unit. Each live map is exactly one `MapInstance`; there is no further spatial subdivision.

### Internal State

| Field | Description |
|---|---|
| `Dictionary<ObjectGuid, ICharacter> _characters` | Active players |
| `Dictionary<ObjectGuid, ICreature> _creatures` | Active creatures |
| `ISpellQueueSystem _spellSystem` | Scoped to this instance |
| `ICreatureRespawner _creatureRespawner` | Receives `ISimulationContext = this` |
| `List<(MapRegion, IMapNavigator)> _navigators` | One navmesh per map region |

### Update Loop

```
1. _creatureRespawner.Update(deltaTime)
2. foreach character → connection.Update(MapSessionFilter) + character.Update(deltaTime)
3. _spellSystem.Update(deltaTime, objectSpells)
4. foreach creature → creature.Script?.Update(deltaTime)
5. foreach character → CharacterGameState.Update(_creatures, _characters, objectSpells)
6. foreach character → BroadcastStateTo(character)
```

`RemoveCharacter` sets `LastEmptyAt = DateTime.UtcNow` when the last player leaves.  
`AddCharacter` clears `LastEmptyAt = null`.

---

## InstanceRegistry

`InstanceRegistry` (`src/Server/Avalon.World/Instances/InstanceRegistry.cs`) owns all live instances.

### Town Routing

`GetOrCreateTownInstance`:
1. Filter active instances by `TemplateId` and `MapType == Town`
2. Pick the one with the lowest `PlayerCount` that passes `CanAcceptPlayer(maxPlayers)`
3. If none found (all full or none exist): create a new `MapInstance`, call `SpawnStartingEntities`, register it

### Normal Map Re-entry

`GetOrCreateNormalInstance`:
1. Check account's existing instance map — if the Guid maps to an existing, non-expired instance: return it
2. Otherwise: create a new `MapInstance` (`OwnerAccountId = accountId`), register it

### Expiry Cleanup

`ProcessExpiredInstances(expiry)`:
- Removes Normal map instances where `IsExpired(expiry)` and `PlayerCount == 0`
- Logs: `"Normal map instance {InstanceId} for map {TemplateId} freed after expiry"`

---

## World Update

```csharp
public void Update(TimeSpan deltaTime)
{
    GameTime.UpdateGameTimers(deltaTime);

    foreach (IMapInstance instance in InstanceRegistry.ActiveInstances)
        instance.Update(deltaTime);

    InstanceRegistry.ProcessExpiredInstances(TimeSpan.FromMinutes(15));
}
```

Town instances are pre-created at startup via `World.LoadAsync`. Normal map instances are spawned on demand by `EnterMapHandler`.

---

## Map Transitions

### Packets

`CEnterMapPacket` (`CMSG_ENTER_MAP`) — client requests a map transition:
```csharp
public class CEnterMapPacket : Packet
{
    public ushort TargetMapId { get; set; }
}
```

`SMapTransitionPacket` (`SMSG_MAP_TRANSITION`) — server response:
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

### `EnterMapHandler` Flow

```
[PacketHandler(NetworkPacketType.CMSG_ENTER_MAP)]

1.  Guard: connection.InGame — else ignore
2.  Resolve current instance from InstanceRegistry
3.  Load MapTemplate for packet.TargetMapId
4.  Find portal from current map where TargetMapId == packet.TargetMapId
5.  No portal → send MapNotFound, return
6.  Proximity check: Vector3.Distance(character.Position, portal.Position) <= portal.Radius
       → too far: send NotNearPortal, return
7.  Level check → send LevelTooLow / LevelTooHigh if out of range
8.  Resolve target instance:
       Town   → GetOrCreateTownInstance(targetMapId, maxPlayers)
       Normal → GetOrCreateNormalInstance(accountId, targetMapId)
9.  world.TransferPlayer(connection, targetInstance):
       a. currentInstance.RemoveCharacter(connection)
       b. character.Map = targetMapId; character.Position = spawn position
       c. targetInstance.AddCharacter(connection)
10. Send SMapTransitionPacket(Success, instanceId, spawnPosition, ...)
11. Enqueue DB update for character.Map + position
```

---

## Logout from Normal Map

`World.DeSpawnPlayerAsync` redirects the character to their home town when they were in a Normal map:

```csharp
IMapInstance? instance = InstanceRegistry.GetInstanceById(connection.Character.InstanceIdGuid);
if (instance?.MapType == MapType.Normal)
{
    MapTemplate? template = _mapManager.Templates.FirstOrDefault(t => t.Id == instance.TemplateId);
    if (template?.LogoutMapId is { } logoutMapId)
    {
        MapTemplate? town = _mapManager.Templates.FirstOrDefault(t => t.Id == logoutMapId);
        dbCharacter.Map = logoutMapId.Value;
        dbCharacter.X   = town?.DefaultSpawnX ?? 0f;
        dbCharacter.Y   = town?.DefaultSpawnY ?? 0f;
        dbCharacter.Z   = town?.DefaultSpawnZ ?? 0f;
    }
}
```

The instance is **not** freed on logout — its `LastEmptyAt` timer governs cleanup independently.

---

## Data Model

### `MapTemplate`

| Field | Description |
|---|---|
| `MapType MapType` | `Town` or `Normal` |
| `float DefaultSpawnX/Y/Z` | Where players appear when entering this map |
| `MapTemplateId? LogoutMapId` | Normal maps point to their home town; null for towns |

### `MapPortal`

```csharp
public class MapPortal
{
    public int Id { get; set; }
    public MapTemplateId SourceMapId { get; set; }
    public MapTemplateId TargetMapId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Radius { get; set; }
}
```

Stored in the `map_portals` table (EF migration `AddInstancedMapSystem`).

---

## Test Coverage

| Scenario | Expected Result |
|---|---|
| Server startup | At least one `MapInstance` (Town) created per town template |
| Character select | Player spawns in correct town instance; `SCharacterSelectedPacket.MapInfo.InstanceId` is a real (non-random) Guid |
| Portal enter (town → normal) | `CEnterMapPacket` near a portal creates a new normal instance; client receives `SMapTransitionPacket(Success)` |
| Portal — too far | `NotNearPortal` result; no transfer |
| Normal map re-entry | Leaving and re-entering within 15 min returns the same `InstanceId` |
| 15-min expiry | `ProcessExpiredInstances` frees the instance; next entry creates a fresh one |
| Town overflow (`MaxPlayers = 2`, 3 connections) | Two instances created; instances have 2 and 1 player respectively |
| Logout from normal map | `character.Map` saved as town map ID; character logs in at town on next session |
