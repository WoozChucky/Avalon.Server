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

Both factory methods are async because a build (layout reads, the doodad plan, the navmesh bake, construction, creature
placement and doodad placement) runs off the tick; the finished instance is published on the tick, and the task
completes then (#639, [world simulation](world-simulation.md)). Party instances come from the World-side
`IPartyInstanceRegistry` (`GetOrCreatePartyInstanceAsync`), which `InstanceRegistry` also implements. So does the
World-side `IInstanceChoices` (`World.InstanceChoices`, #1104), for choosing an instance at a portal:
`CreatePrivateInstanceAsync` and `CreatePartyInstanceAsync` (a build of its own, never joined), `FindChoosable` (an
instance by id, only if it is the character's own live private one of the map or one of its party's), `ListChoosable`
(those instances, each flagged whether a plain entry would take it) and `MakeDefault`. All of it is tick thread only,
and it reads only the asking character's and its party's lists, never every instance.

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
| `MapLayoutEntries LayoutEntries` | `Layout` as that packet carries it: an entry per chunk, with its name, and per portal. Built in the constructor, never changed (see "What an arrival is sent") |

### Update Loop

An instance nobody is in stands still: it drops its finished projectiles and pauses its auras, and nothing else runs.
Otherwise, in this order:

```
0. resume paused auras; rescale creature health when the party size changed;
   send owed loot snapshots and PvP states to characters that just arrived
1. _corpseRemover.Update(deltaTime); expired item-use summons leave; due doodad respawns (a min-heap peeked once)
2. foreach character → its in-map packets (MapSessionFilter) + character.Update(deltaTime)
   then the vendor pass, once a shop has been opened here
3. combat: the ability cast system, the aura pass, item and doodad cast bars, the combat service, threat broadcasts
4. creature scripts, then the locomotion that executes what they decided
5. snapshot dirty fields, then per character: its view (decided on the broadcast tick, #981) and the state broadcast
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

An owner, a character or a party, may hold **several live instances of the same map** (#1104). The registry keeps a
list per owner and map (character → map → instances, party → map → instances) in default order: the **last live,
unexpired entry is the default**, the instance a plain entry takes.

`GetOrCreateNormalInstanceAsync`:
1. Walk the character's list for the map from the end and return the first instance still live and not expired: the
   default (party instances: the party's list, the same rule, so when the party's newest instance has expired while an
   older one is still live, the party is routed to the older one)
2. Otherwise: build a new `MapInstance` via `ChunkLayoutInstanceFactory.BuildAsync(template, ownerCharacterId, ct)` (same factory used for towns; the layout source resolver picks `ProceduralChunkLayoutSource` for `MapType.Normal`), register it, and
   append it to the list, so it becomes the default

**Creates.** `CreatePrivateInstanceAsync` and `CreatePartyInstanceAsync` always start a build of their own: no request
joins it, and it joins none (they are kept apart from the per-owner de-duplicated builds, under the same deadline). Once
published the instance is **inserted at the front** of its owner's list: listed, but not the default until
`MakeDefault` moves it to the end (the portal entry does that on arrival). A create that fails or times out leaves the
list as it was. `MakeDefault` on an instance its owner no longer lists (freed, or its party forgotten) does nothing.

The lists are keyed by `characterId` (the in-game character id surfaced via `ObjectGuid.Id`), NOT by `accountId`. Two characters on the same account that walk into the same procedural map within the re-entry window get separate instances.

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
- Takes each freed instance out of its owner's list (`RemoveInstance` does the same), dropping a list, and an owner,
  left empty; an older instance of the same owner and map stays listed, and becomes the default if it is the last live
  one.
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

### Build deadline

A build has 30 s (`InstanceRegistry.BuildDeadline`, #969, owner decision): long enough for the slowest forest bake
under load (#924), short enough for a player waiting on a move. Without it a build that never finished (a bake that
hangs, a task never completed) kept every requester's move under way, and so the character unable to take a portal,
return to town, teleport or read a town scroll, for as long as it stayed in the world.

- **Timed on the world's clock, acted on on the tick.** A build still running once it is started gets a timer on the
  container's `TimeProvider`; when it fires it only queues the deadline beside the finished builds, and
  `PublishFinished` acts on it on the tick. Nothing is scanned per tick: a tick with no deadline due and no build
  finished costs what it did. The timer is disposed when the build's outcome is published.
- **Abandoned, not cancelled.** At the deadline the build's pending entry is dropped, so the next request starts a new
  build at once, and its task is faulted with an `InstanceBuildTimeoutException`. Nothing reaches into the bake, which
  runs on; if it ever ends, its outcome is discarded where it would have been published: an instance it built is
  disposed without ever being registered or indexed (one Warning naming it and how long the build took), and a
  failure is logged at Warning. A deadline that fires while the build's outcome is already queued finds the requesters
  answered and does nothing.
- **Every requester ends its move through its failure path.** A portal entry's arrival, a return to town
  (`TownReturn`: a respawn, a town scroll, a party's leave countdown) and an item's teleport (`MapTeleport`) take the
  path a failed build takes, which clears `RespawnInFlight`, and send the player, once, the system chat line "The way
  could not be opened. Please try again." (`InstanceBuildTimeoutException.PlayerMessage`), stamped with the time the
  build was abandoned. A party's leave countdown, which tries its return again itself as for any failed return
  ([parties](parties.md), #700), sends "The way could not be opened. Trying again…"
  (`InstanceBuildTimeoutException.RetryingMessage`, owner decision) instead. Each logs the timeout at Warning, without
  the stack. A character select waiting on a town build is stopped, as before, by
  the stalled-select sweep (`Game:CharacterLoadTimeoutSeconds`, 15 s by default), and is told nothing more.
- **Counted and logged as a server fault.** Each abandoned build adds one to `world.instance.build_timeouts`, tagged
  `map.type` ([instrumentation](instrumentation.md#instance-build-timeouts)), and writes one Error: the build's kind,
  map and owner, how many requests were waiting on it and how many abandoned builds are still running (its instance id
  is not known yet; the Warning of a late outcome names it). That running count is also the gauge
  `world.instance.builds_abandoned_running` (`InstanceRegistry.AbandonedBuildsRunning`): up at each abandonment, down
  when the late outcome arrives, so bakes that never end show as a count that keeps climbing.

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

The numbers are those of the handler's own step comments (`src/Server/Avalon.World/Handlers/EnterMapHandler.cs`), so
"step 10" means the same thing here and there. Steps 1 to 7 run as the packet is handled, up to the request for the
target instance; the arrival checks listed under step 7 and steps 8 to 13 run in the connection continuation, once
that instance is ready. A refusal is an `SMapTransitionPacket` whose `Result` is the `MapTransitionResult` named and
whose other fields are empty (`SMapTransitionPacket.CreateFailure`). Each refusal a client can cause by what it sends
(a dead character, a move under way, an unknown map, no portal, too far from the portal) writes one Debug line, built
only when Debug is written, as a packet refused for the connection's current state does
([packet handlers](packet-handlers.md)). Every refusal, at whichever step, is counted in
`world.portal_entry.refusals` under its `reason` ([instrumentation](instrumentation.md#refused-portal-entries)): a
world does not write Debug as shipped, so the count is what shows, for one, clients and the server disagreeing about
where characters stand (`not_near_portal`).

```
[PacketHandler(NetworkPacketType.CMSG_ENTER_MAP)]

1.  Guards: not connection.InGame → ignored, no reply; a dead character → ignored, no reply
    Another move under way (connection.RespawnInFlight: a scroll return, an item teleport,
       a respawn, a party return, or an earlier portal entry that has not ended) → send
       MoveInProgress, return, before anything is looked up
2.  Resolve the current instance: InstanceRegistry.GetInstanceById(character.InstanceId)
3.  Find the MapTemplate of packet.TargetMapId among world.MapTemplates, by id
       (MapTemplateRows.FindById: the map manager holds the templates with a lookup by id)
       → none: send MapNotFound, return
4.  Find the portal. The current instance must be a live MapInstance
       → not one: send MapNotFound, return
    Look up the PortalInstance on mi.Portals where TargetMapId == packet.TargetMapId
       (portals are surfaced from ChunkLayout.Portals at instance build time —
        single source of truth, no DB fallback)
       → no portal: send MapNotFound, return
5.  Proximity check: Vector3.Distance(character.Position, portal.Position) <= portal.Radius
       → too far: send NotNearPortal, return
6.  Level checks against the template's MinLevel and MaxLevel, each only when set
       → send LevelTooLow / LevelTooHigh, return
7.  The request is accepted: set connection.RespawnInFlight (the move is under way until it
       ends, see below), then resolve the target instance (built off the tick when there is
       none to reuse):
       Town   → GetOrCreateTownInstanceAsync(templateId, MaxPlayers ?? 30)
       Normal → in a party: GetOrCreatePartyInstanceAsync(partyId, templateId), then re-check the
                party (MapNotFound) and the seats (InstanceFull) once it is built;
                otherwise GetOrCreateNormalInstanceAsync(characterId, templateId)
       The arrival continuation first checks the character is still the connection's (a select
       on another connection can take it meanwhile: nothing is done, no reply), then that it is
       still alive (one that died meanwhile is not moved: no reply), then that the
       instance is still registered; one released meanwhile is resolved again once (see Expiry
       Cleanup), and a second release in a row → send MapNotFound, return
8.  Exit path: drop the character from any encounter in progress on the source instance
       (CombatService.DropPlayerFromEncounter); the transfer moves instance membership only
9.  world.TransferPlayer(connection, targetInstance):
       a. currentInstance.RemoveCharacter(connection)
       b. character.InstanceId = targetInstance.InstanceId
       c. targetInstance.AddCharacter(connection) — connection.LastInputSeq is not reset: it
          stays monotonic for the connection's life
    It also ends an open NPC conversation and a bank or shop opened in it, before (a).
    It does not move the character: the position is the row's, written in step 13
10. Resolve the spawn (MapArrival.SpawnOf): the target MapInstance's EntrySpawnWorldPos (its
       layout's entry spawn); the template's DefaultSpawnX/Y/Z only for a target that is not a
       MapInstance
11. Send SMapTransitionPacket(Success, instanceId, mapId, spawn, map name and description)
12. Send SChunkLayoutPacket for the new instance (seed, cell size, chunks, entry spawn, portals)
       when the target is a MapInstance, in the same continuation as 9 and 11: the instance
       sends its loot snapshot on its next tick, and the client has to know the map first
    11 and 12 are MapArrival.Send, the one place every arrival's packets are sent from (below)
13. Save the character (ICharacterSaver.Save: the row's map, instance and the spawn of step 10
       as its position, with any dirty inventory and money)
```

### A portal entry is a move under way

From step 7 until it ends, an accepted portal entry holds `connection.RespawnInFlight`, as a return to town
(`TownReturn`), a teleport (`MapTeleport`) and a respawn hold it: the continuation that arrives runs after the packet
pass, so without it a second `CMSG_ENTER_MAP` handled in the same pass, or while the instance is still being built,
would pass the checks the first one passed and arrive, and save, once more. The second is answered `MoveInProgress`
in step 1, and so is any other move asked for meanwhile.

Every ending clears it. One that reaches the arrival is cleared in `EnterMapHandler.Arrive`, in a `finally`, on
every path out of it; the one that does not (asking for the instance throws, so no arrival is queued) is cleared in
`Execute`'s `catch`:

| Ending | What the client is sent | The flag |
|---|---|---|
| The arrival (steps 8 to 13) | the transition and the layout | cleared |
| The character died while the instance was being built | nothing, as a dead character's request in step 1: it is not moved, and respawns from where it died | cleared |
| The instance was released before the arrival, the first time | nothing yet: the target is resolved again | kept: the second resolve is the move now, and ends as any other |
| Released a second time | `MapNotFound` | cleared |
| No longer in the party the instance is for, or the instance is full | `MapNotFound`, `InstanceFull` | cleared |
| The instance could not be built (its request faulted or was cancelled) | nothing, as before; logged at Error by the arrival, on the tick, once, before any other check: also when the character died or left meanwhile, since nothing else logs a failed build | cleared, in the same `finally`: a log provider that throws does not keep it (for a character that left, as that row says) |
| The build ran past its deadline and was abandoned (#969) | no transition; the system line "The way could not be opened. Please try again." (not to a character that left meanwhile); logged at Warning by the arrival, since the registry logged the timeout at Error | cleared, as for any failed build |
| A step of the arrival throws (the transfer, a send) | whatever was sent before it | cleared; the throw is logged by the continuation drain |
| Asking for the instance throws, before anything is queued | nothing; the throw is logged by the packet dispatch | cleared |
| The character left the connection meanwhile | nothing | left alone: the connection cleared it as the character left (`ResetCharacterState`), and it may be the next character's |

The arrival is queued on the connection to run whatever becomes of the request for the instance
(`ISettledContinuations.EnqueueSettled`, World-side: an interface of `Avalon.World` that `WorldConnection`
implements, beside the modding API's two continuations and in the same queue). The ordinary continuation would not
do: its drain logs and drops the callback of a task that did not succeed, and the arrival is what ends the move, so
a failed build would leave the flag set for good. `TownReturn`, `MapTeleport` and a chat command's `ctx.Then` get
their callback run another way, by wrapping the task in one that cannot fault, which costs a task per request and
hands the outcome to a thread-pool thread first; they still do. A portal entry's arrival is one queued entry, as the
direct hand-off was before the move was marked, ready the moment the registry completes the build's task: it runs in
the flush of the tick that published the build, and an entry asked for while the instance is still being built
allocates no more than one whose instance already exists
([benchmarks](benchmarks.md#arrival-in-a-map--benchmarksarrivalbenchmarkscs-883)).

A build that never finishes no longer keeps the move under way (#969): at its deadline, 30 s, the registry abandons
it and hands every requester the failure, so the arrival ends the move as for any failed build, and the player is
told with a system line ([Build deadline](#build-deadline)). The same holds for a return to town and a teleport.

A party's leave countdown that runs out while a portal entry is under way finds the move in flight, as it does for
a respawn: it is re-armed as a failed return (logged at Warning, "A return to town was already under way") and
spends one of its retries ([parties](parties.md)).

### What an arrival is sent

Every arrival sends the same two packets from one place, `MapArrival` (`Avalon.World/Respawn`): a portal entry, a
return to town (`TownReturn`) and an item's teleport (`MapTeleport`) call `MapArrival.Send`, the transition and then
the chunk layout; a character select sends its own `SCharacterSelectedPacket` and then `MapArrival.SendLayout`. The
layout packet's chunk and portal entries are built once, with the instance (`MapInstance.LayoutEntries`,
`MapLayoutEntries`, #883): the instance's constructor makes them, so they are built where the instance is, off the
tick, and the first character to arrive costs the tick what every other one does. They come from the instance's
`ChunkLayout`, which it keeps for its life, and from the chunk library's names, loaded once with the world; neither
is something `/reload` or live template editing replaces. Every `MapInstance` has them, as it has a layout (#912): a
client completes a transition on the layout it is sent behind it, so a `Success` with no layout must not be
possible. The constructor requires the chunk library from its container, as it requires the combat settings and the
script manager (`WorldHostGraphShould` holds the registration): without one the build fails, and so it does for a
chunk the library does not hold, as the navmesh bake before it already did. The entries are written into each
packet and never changed, and `MapArrivalShould` holds every arrival's packet to the layout byte for byte.

---

## Logout from Normal Map

`World.DeSpawnPlayerAsync` redirects the character to their home town when they were in a Normal map:

```csharp
IMapInstance? instance = InstanceRegistry.GetInstanceById(connection.Character.InstanceIdGuid);
if (instance?.MapType == MapType.Normal)
{
    MapTemplate? template = _mapManager.Templates.FindById(instance.TemplateId);
    if (template?.LogoutMapId is { } logoutMapId)
    {
        MapTemplate? town = _mapManager.Templates.FindById(logoutMapId);
        dbCharacter.Map = logoutMapId;
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
| `float DefaultSpawnX/Y/Z` | The position saved on the row when a logout moves the character to this town (above). An arrival appears at the entry spawn of the instance's layout instead; these serve an arrival only when its target is not a `MapInstance` (step 10) |
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

### The broadcast pairing

`BroadcastStateTo` and the two methods it calls, `DescribeNewObject` and `DescribeUpdatedObject`, decide what each
client is sent of each entity; `MapInstance.MaskForRecipient` is the recipient-dependent part for a character (its own
client is not sent its motion, and no other client is sent its experience, #993). Tests drive a real `MapInstance` and
assert what each connection receives, rather than a field selection the test chose: `CharacterDeltaReplicationShould`
(deltas, the owner's motion left out, experience to the owner alone) and `StateUpdateEncodingShould` (every recipient's
packets byte for byte against the per-recipient encoding). `ObjectStateWriterShould` covers the writer one layer down,
over `MaskForRecipient` for the character scenarios. Dropping the mask from either describe method fails them.
