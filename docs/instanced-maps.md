# Instanced Map System

This document describes the instanced map architecture used by the World server.

---

## Overview

Avalon has no single persistent open world. Players gather in shared town instances and play in private instances, created on demand and freed after a period of disuse.

> **Map geometry / authoring:** instances are built from chunk-composed layouts — the same pipeline drives both town and procedural maps. See **[Map Generation](map-generation.md)** for chunk authoring, town layout authoring, the `ChunkLayoutInstanceFactory` build flow, and the `SChunkLayoutPacket` wire format. This document covers instance lifecycle (creation, routing, transitions, expiry); the geometry stack lives in the map-generation doc.

### Map Types

| Type    | Description |
|---------|-------------|
| `Town`  | Shared hub with a player cap (default 30). Multiple instances are created automatically when all existing ones are full. New players are always routed to the least-populated instance that still has room. |
| `Normal`| Private instanced area, one per player, or one per party while the character is in a party (see [party protocol](party-protocol.md) and [parties](parties.md)). An expiry countdown of `Game:AbandonedInstanceLifetimeMinutes` (15 by default) starts when the last player leaves. Re-entering within that window returns the player to the same live instance. After expiry the instance is freed; at 0 it is freed on the next tick and every entry builds a new one (see [Expiry Cleanup](#expiry-cleanup)). |

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
    Normal = 1,   // Private instanced map; expires once abandoned (Game:AbandonedInstanceLifetimeMinutes)
}
```

### `ISimulationContext`

```csharp
// src/Server/Avalon.World.Public/Instances/ISimulationContext.cs
public interface ISimulationContext
{
    IReadOnlyDictionary<ObjectGuid, ICharacter> Characters { get; }
    IReadOnlyDictionary<ObjectGuid, ICreature>  Creatures  { get; }
    ICombatService      CombatService { get; }
    ICreatureLocomotion Locomotion    { get; }
    IMeleeSlots         MeleeSlots    { get; }
    IMapNavigator GetNavigatorForPosition(Vector3 position);

    bool QueueAbility(IUnit caster, AbilityAim aim, IAbility ability);
    bool RunInstantAbility(IUnit caster, AbilityAim aim, IAbility ability);
    void AddCreature(ICreature creature);
    void RemoveCreature(ICreature creature);

    void BroadcastUnitHit(IUnit attacker, IUnit target, uint currentHealth, uint damage);
    void BroadcastAttackAnimation(IUnit attacker, IAbility? ability);
    void BroadcastFinishCast(IUnit caster, IAbility ability);
    void BroadcastInterruptedCast(IUnit caster, IAbility ability);
    void BroadcastUnitDeath(IUnit unit, IUnit? killer);
    void BroadcastUnitRevive(IUnit unit, Vector3 position, uint health);
}
```

`ISimulationContext` is the minimal contract creature AI scripts and the ability system use to reach their instance.
`MapInstance` is the sole simulation unit — there is no sub-map spatial division. Creatures do not respawn: a corpse
is removed after its `BodyRemoveTimer`.

### `IMapInstance`

```csharp
// src/Server/Avalon.World.Public/Instances/IMapInstance.cs
public interface IMapInstance : ISimulationContext
{
    Guid          InstanceId       { get; }
    MapTemplateId TemplateId       { get; }
    MapType       MapType          { get; }
    int           Seed             { get; }
    string        ConfigVersion    { get; }
    uint?         OwnerCharacterId { get; }            // null for town and party instances
    IReadOnlyList<uint> AllowedCharacters { get; }
    int           PlayerCount      { get; }
    DateTime?     LastEmptyAt      { get; }            // null while any player is inside

    bool IsExpired(TimeSpan expiry);
    bool CanAcceptPlayer(ushort maxPlayers);

    void AddCharacter(IWorldConnection connection);
    void RemoveCharacter(IWorldConnection connection);
    void Update(TimeSpan deltaTime);
}
```

A party instance names its party in `MapInstance.OwnerPartyId`, World-side ([parties](parties.md)).

### `IInstanceRegistry`

```csharp
// src/Server/Avalon.World.Public/Instances/IInstanceRegistry.cs
public interface IInstanceRegistry
{
    IReadOnlyCollection<IMapInstance> ActiveInstances { get; }

    /// <summary>Returns the least-full town instance. Creates a new one if all are at capacity.</summary>
    Task<IMapInstance> GetOrCreateTownInstanceAsync(MapTemplateId templateId, ushort maxPlayers);

    /// <summary>Returns the character's existing live instance if within expiry window; else creates a new one.
    /// Keyed by character (not account) so alts on the same account get separate instances.</summary>
    Task<IMapInstance> GetOrCreateNormalInstanceAsync(uint characterId, MapTemplateId templateId);

    IMapInstance? GetInstanceById(Guid instanceId);
    void RemoveInstance(Guid instanceId);
    void ProcessExpiredInstances(TimeSpan abandonedInstanceLifetime);
}
```

Both factory methods are async because a build (layout reads, the navmesh bake, construction and creature placement)
runs off the tick; the finished instance is published on the tick, and the task completes then (#639,
[world simulation](world-simulation.md)). Party instances come from the World-side `IPartyInstanceRegistry`
(`GetOrCreatePartyInstanceAsync`), which `InstanceRegistry` also implements.

---

## MapInstance

`MapInstance` (`src/Server/Avalon.World/Instances/MapInstance.cs`) is the core simulation unit. Each live map is exactly one `MapInstance`; there is no further spatial subdivision.

### Internal State

| Field | Description |
|---|---|
| `_characters`, `_creatures` | Active players and creatures, by `ObjectGuid` |
| `_abilityCastSystem` | The instance's `InstanceAbilityCastSystem` (casts and projectiles) |
| `_auras` | The instance's `AuraSystem` |
| `_corpseRemover` | Removes corpses after their `BodyRemoveTimer`; creatures never respawn |
| `MapNavigator _navigator` | Single combined navmesh for the whole instance, baked from the chunk layout |
| `ChunkLayout Layout` | Authoritative layout (chunks, entry spawn, portals) sent to clients via `SChunkLayoutPacket` |

### Update Loop

An instance nobody is in stands still: it drops its finished projectiles and pauses its auras, and nothing else runs.
Otherwise, in this order:

```
0. resume paused auras; rescale creature health when the party size changed;
   send owed loot snapshots and PvP states to characters that just arrived
1. _corpseRemover.Update(deltaTime); expired item-use summons leave
2. foreach character → its in-map packets (MapSessionFilter) + character.Update(deltaTime)
   then the vendor pass, once a shop has been opened here
3. combat: the ability cast system, the aura pass, item cast bars, the combat service, threat broadcasts
4. creature scripts, then the locomotion that executes what they decided
5. snapshot dirty fields, then per character: visibility and the state broadcast
```

`RemoveCharacter` sets `LastEmptyAt` from the instance's `TimeProvider` when the last player leaves, and so does
construction, so an instance nobody ever entered expires too. `AddCharacter` clears it and marks the instance entered
(`HasBeenEntered`, World-side), which decides which lifetime its expiry counts against (see [Expiry Cleanup](#expiry-cleanup)).

---

## InstanceRegistry

`InstanceRegistry` (`src/Server/Avalon.World/Instances/InstanceRegistry.cs`) owns all live instances.

### Town Routing

`GetOrCreateTownInstanceAsync`:
1. Filter active instances by `TemplateId` and `MapType == Town`
2. Pick the one with the lowest `PlayerCount` that passes `CanAcceptPlayer(maxPlayers)`
3. If a build of that map is already under way, return its task (#442)
4. Otherwise: start a build off the tick, `ChunkLayoutInstanceFactory.BuildAsync(template, ownerCharacterId: null, ct)`
   (which places the town's authored creatures), and register the instance when it is published on the tick

### Normal Map Re-entry

`GetOrCreateNormalInstanceAsync`:
1. Check the character's existing instance map — if the Guid maps to an existing, non-expired instance: return it
   (party instances: the party's map, the same rule)
2. Otherwise: build a new `MapInstance` via `ChunkLayoutInstanceFactory.BuildAsync(template, ownerCharacterId, ct)` (same factory used for towns; the layout source resolver picks `ProceduralChunkLayoutSource` for `MapType.Normal`), register it

The cache is keyed by `characterId` (the in-game character id surfaced via `ObjectGuid.Id`), NOT by `accountId`. Two characters on the same account that walk into the same procedural map within the re-entry window get separate instances.

### Expiry Cleanup

`ProcessExpiredInstances(abandonedInstanceLifetime)` runs on every tick, at the end of `World.Update`:
- Removes empty Normal map instances (solo and party) past their lifetime, and disposes them (the navmesh goes with
  them):
  - **abandoned** (a player entered it, and it is now empty): `Game:AbandonedInstanceLifetimeMinutes`, 15 by default.
    The re-entry lookups (`GetOrCreateNormalInstanceAsync`, `GetOrCreatePartyInstanceAsync`) use the same value, so
    an instance past it is never handed out again. A party instance stays alive while any member is inside.
  - **never entered**: a fixed 15 minutes (`InstanceRegistry.UnenteredInstanceLifetime`), whatever the setting. A
    build is published at the start of `World.Update` and its requester enters it in a connection continuation after
    the expiry pass, so at 0 it would otherwise be freed before anyone arrived. A disbanded party's orphaned build
    expires on this clock too.
- Logs: `"Normal map instance {InstanceId} for map {TemplateId} freed after expiry"`

**At 0** (the load-test world): an emptied dungeon is never reused, and the expiry pass of the tick after the last
player leaves frees it; the next entry builds a new one. Release stays on the tick and goes only through this pass
(no new per-tick cost: one flag read per empty instance in the existing walk).

**Arrival guard.** A portal entry (`EnterMapHandler`) or an item teleport (`MapTeleport`) resolves its instance on one
tick and adds the character in a later continuation. If the instance was released in between (at 0, the last member
inside a party instance can leave meanwhile), the character is never added to it: the arrival resolves the target
again, once, through the same path (the party's instance for a member, the character's own otherwise) and enters
that. A second release in a row, which nothing is known to cause, answers `MapNotFound`. Towns are never released by
expiry, so their arrivals never take this path.

---

## World Update

`World.Update` advances the game time, applies queued `/reload` patches and any script hot reload, then
hands every live instance to `InstanceTicker.Tick`, and finally frees expired normal instances
(`ProcessExpiredInstances` with `Game:AbandonedInstanceLifetimeMinutes`, read once at start). The live instances it ticks are
`InstanceRegistry.TickInstances` (#851): an array in publication order, rebuilt only after an instance
is published or removed, so a tick that does neither allocates nothing for the walk.

`InstanceTicker` (#639) ticks each instance on its own:

- **Contained.** An instance whose `Update` throws is logged at Error, and the other instances still
  tick, as do the flushes the tick loop runs after the world update. Before, the exception left
  `World.Update`: every instance after the broken one went unticked, and so did that tick's inventory,
  sheet and outbox flushes and continuations. The instance stays live and is ticked again next tick.
  One that keeps throwing is logged at most once per 10 s (`InstanceTicker.FailureLogInterval`), with
  how many throws were left out since.
- **Timed.** Each update is recorded in `world.instance.update.duration` (microseconds) and each throw
  counted in `world.instance.update.failures`, both on the World meter and tagged `map.type` (`Town` or
  `Normal`; never the instance id, which would make the series unbounded). This separates the cost of
  one busy instance, such as a crowded town, from the cost of many, which is the measurement #639 needs
  before ticking instances in parallel.

No instance is built at startup. A town's first instance is built the first time a character needs it (a select, a portal, a respawn or a return to town); a normal map's when a character takes a portal to it (`EnterMapHandler`) or an item teleports it there.

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
    Success          = 0,
    MapNotFound      = 1,
    NotNearPortal    = 2,
    LevelTooLow      = 3,
    LevelTooHigh     = 4,
    GenerationFailed = 5,
    InstanceFull     = 6,   // a party's instance is full (docs/party-protocol.md)
    NoWalkableGround = 7,   // an item teleport found no ground (docs/item-use-protocol.md)
    MoveInProgress   = 8,   // another move to a map is already under way
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

1.  Guard: connection.InGame — else ignore; a dead character — ignore
    Another move under way (connection.RespawnInFlight: a scroll return, an item teleport,
       a respawn or a party return) → send MoveInProgress, return, before anything is looked up
2.  Resolve current instance from InstanceRegistry (must be a MapInstance with Layout)
3.  Load MapTemplate for packet.TargetMapId
4.  Look up matching PortalInstance on mi.Portals where TargetMapId == packet.TargetMapId
       (portals are surfaced from ChunkLayout.Portals at instance build time —
        single source of truth, no DB fallback)
5.  No portal → send MapNotFound, return
6.  Proximity check: Vector3.Distance(character.Position, portal.Position) <= portal.Radius
       → too far: send NotNearPortal, return
7.  Level check → send LevelTooLow / LevelTooHigh if out of range
8.  Resolve target instance:
       Town   → GetOrCreateTownInstanceAsync(targetMapId, maxPlayers)
       Normal → in a party: GetOrCreatePartyInstanceAsync(partyId, targetMapId), then re-check the
                party (MapNotFound) and the seats (InstanceFull) once it is built;
                otherwise GetOrCreateNormalInstanceAsync(characterId, targetMapId)
       The arrival continuation first checks the instance is still registered; one released
       meanwhile is resolved again once (see Expiry Cleanup)
9.  world.TransferPlayer(connection, targetInstance):
       a. currentInstance.RemoveCharacter(connection)
       b. character.InstanceId = targetInstance.InstanceId
       c. targetInstance.AddCharacter(connection) — also resets connection.LastInputSeq = 0
10. Send SMapTransitionPacket(Success, instanceId, spawnPosition, ...) followed by
    SChunkLayoutPacket for the new instance
11. Save the character (ICharacterSaver.Save: the row's map, instance and position)
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

Portals come from `ChunkLayout.Portals` populated at instance build time. See [Map Generation](map-generation.md) for the per-placement routing config.

---

## Test Coverage

| Scenario | Expected Result |
|---|---|
| Character select | Player spawns in correct town instance; `SCharacterSelectedPacket.MapInfo.InstanceId` is that instance's id |
| Portal enter (town → normal) | `CEnterMapPacket` near a portal creates a new normal instance; client receives `SMapTransitionPacket(Success)` |
| Portal — too far | `NotNearPortal` result; no transfer |
| Normal map re-entry | Leaving and re-entering within the lifetime returns the same `InstanceId`; at 0 the emptied instance is freed and the next entry builds a new one, while a build nobody has entered yet survives the pass (`InstanceRegistryShould`) |
| Expiry | `ProcessExpiredInstances` frees the instance; next entry creates a fresh one |
| Town overflow (`MaxPlayers = 2`, 3 connections) | Two instances created; instances have 2 and 1 player respectively |
| Logout from normal map | `character.Map` saved as town map ID; character logs in at town on next session |

### Not covered: the broadcast pairing

`BroadcastStateTo` and the two methods it calls, `DescribeNewObject` and
`DescribeUpdatedObject`, have no automated coverage at all. Entity replication is tested one
layer down, in `ObjectStateWriterShould`: those scenarios pick the field selection themselves
and call `MapInstance.MaskSelfSuppression` directly, so what they verify is the writer and the
helper, never `MapInstance`'s choice of either.

That leaves the pairing unverified in both directions, and both mistakes are silent:

- Swap the selection a kind is described under — `GameEntityFields.CreatureUpdate` for
  `GameEntityFields.CharacterUpdate`, say — and every test stays green.
- Delete the `MaskSelfSuppression` call from either method and every test stays green. Dropped
  from `DescribeUpdatedObject`, that ships a player their own position ten times a second on a
  packet the client does not expect to carry it, fighting whatever the client predicts locally;
  dropped from `DescribeNewObject`, once per entry into view.

The second is the shape of the problem. The writer is well covered; the decision about what to
hand the writer is not covered at all, and that decision is where the player-visible mistakes
are. Closing it takes a test that drives `MapInstance` itself — a character, a second
character, a creature and a portal in one instance — and asserts what each connection is sent,
rather than asserting against a selection the test chose.

The gap is older than the `ObjectState` message and did not arrive with it.
