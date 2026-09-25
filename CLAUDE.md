# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Restore, build, test
dotnet restore
dotnet build --no-restore
dotnet test --no-build

# Run a single test project
dotnet test tests/Avalon.Server.Auth.UnitTests
dotnet test tests/Avalon.Server.World.UnitTests
dotnet test tests/Avalon.Shared.UnitTests
dotnet test tests/Avalon.Api.UnitTests

# Run a specific test class or method
dotnet test tests/Avalon.Server.Auth.UnitTests --filter "FullyQualifiedName~CAuthHandlerShould"

# Start infrastructure (Redis + Postgres)
docker compose up -d redis postgres

# Run individual servers
dotnet run --project src/Server/Avalon.Api
dotnet run --project src/Server/Avalon.Server.Auth
dotnet run --project src/Server/Avalon.Server.World

# Publish (Release)
dotnet publish src/Server/Avalon.Server.World/Avalon.Server.World.csproj -c Release
dotnet publish src/Server/Avalon.Server.Auth/Avalon.Server.Auth.csproj -c Release
dotnet publish src/Server/Avalon.Api/Avalon.Api.csproj -c Release

# Benchmarks
dotnet run -c Release --project tools/Avalon.Benchmarking

# Add a migration (EF design-time)
# Avalon.Api is the startup project by design (early-development convenience).
# Migrations are applied automatically when Avalon.Api starts.
dotnet ef migrations add <Name> \
  --project src/Server/Avalon.Database.Auth \
  --startup-project src/Server/Avalon.Api \
  --context AuthDbContext
```

Target framework: **.NET 10**. Docker compose credentials default to password `123`.

## Architecture

Avalon is split into three independently deployable real-time servers plus a REST API:

| Component | Project | Role |
|---|---|---|
| REST API | `Avalon.Api` | HTTPS/JWT, account management, OpenAPI (Scalar UI at `/scalar`) |
| Auth Server | `Avalon.Server.Auth` | TCP login flow, MFA, world-key issuance |
| World Server | `Avalon.Server.World` | Game simulation loop, packet dispatch, world lifecycle |
| Core world | `Avalon.World` | Instanced maps, entities, spell/creature systems, chat commands |

Shared libraries under `src/Shared/`:
- `Avalon.Network.Tcp` / `.Packets` / `.Packets.Abstractions` — custom TCP server, packet protocol (Protobuf-net)
- `Avalon.Domain` — rich domain model (Account, Character, Spell, Creature, etc.)
- `Avalon.Common` — `ValueObject<T>`, utilities, JSON converters, math
- `Avalon.Configuration` — strongly-typed config binding classes
- `Avalon.Metrics` — OpenTelemetry integration

Infrastructure: `Avalon.Infrastructure` — `IReplicatedCache` (Redis wrapper), `IMFAHashService`, `CacheKeys` (all Redis key strings centralized here), `ISecureRandom`.

Three separate Postgres DbContexts (via Npgsql EF Core): `AuthDbContext`, `CharacterDbContext`, `WorldDbContext`. Design-time factories in each `Avalon.Database.*` project enable `dotnet ef` without a running host.

## Packet Protocol

All client↔server communication is custom TCP with Protobuf-net. Every packet wraps a `NetworkPacket` (header + payload). The header carries `NetworkPacketType` (opcode), `NetworkPacketFlags` (encryption/compression bitmask), `NetworkProtocol` (channel grouping), and `Version`.

**Adding a packet handler:**
1. Define the packet contract in `Avalon.Network.Packets` with a `NetworkPacketType` enum value.
2. For Auth: implement `IAuthPacketHandler<TPacket>` in `Avalon.Server.Auth/Handlers/`.
3. For World (server layer): implement `IWorldPacketHandler<TPacket>` in `Avalon.Server.World/Handlers/`, decorated with `[PacketHandler(NetworkPacketType.X)]` — handlers are registered via reflection scan in `WorldServer` constructor.
4. For World (core layer): implement `WorldPacketHandler<TPacket>` in `Avalon.World/Handlers/`, also discovered by attribute scan.

Auth handlers are registered in DI and resolved manually; World handlers use `ActivatorUtilities.CreateInstance` in `WorldServer`.

**A new client→server opcode also needs a session filter entry, or the handler never runs.** `MapSessionFilter` (in-map packets) or `WorldSessionFilter` (pre-character packets) must accept the opcode, not just a registered handler for it. `WorldConnection.OnReceive` only queues a packet if a filter accepts it at arrival; one no filter ever accepts skips the queue entirely and is dropped with a "could not find a handler" warning — a missing filter entry fails safe, so debug it by grepping the logs, not by hunting a frozen connection. The queue *can* wedge, but only for the narrower race where a packet's acceptance changes between arrival and dispatch (e.g. `CMSG_CHARACTER_LIST`/`CMSG_CHARACTER_LOADED` around a character spawn) — see `ProcessQueueWedgeShould.cs`.

**Reflection-bound registration — a reference grep proves nothing.** Packet handlers are discovered by attribute scan, and AI/ability scripts by type name: `ScriptManager` keys every `AiScript` subclass by `t.Name` (except those marked `[ChainedScript]`), and `ICreaturePlacementService.AttachScript` resolves `creature.ScriptName` from the DB and builds it with `ActivatorUtilities.CreateInstance(_sp, scriptType, creature, instance)`. So "nothing references this type" says nothing about whether it is used — and note that call site passes exactly two runtime arguments, so a script constructor needing anything beyond `(ILoggerFactory, ICreature, ISimulationContext)` will throw and be swallowed into a logged error. Check constructibility against that call site, not against grep — `AiScriptConstructibilityShould` does exactly that for every nameable script, against the production container.

## Auth Flow

```
CClientInfoPacket → SHandshakePacket → CHandshakePacket → SHandshakeResultPacket
→ CAuthPacket → SAuthResultPacket (BCrypt verify, lockout, then non-Active Status refused as BANNED/DEACTIVATED, then MFA check)
→ CWorldListPacket → SWorldListPacket
→ CWorldSelectPacket → SWorldSelectPacket (non-Active Status closes the connection; CSPRNG world key written to Redis, SETNX inWorld mutex)
→ [new TCP to World] CExchangeWorldKeyPacket → SExchangeWorldKeyPacket (key consumed from Redis, access re-checked, inWorld mutex cleared)
```

**Spending the world key** (`ExchangeWorldKeyHandler`): the `DEL` spends the key, not the `GET`. Two connections presenting the same key can both read it, so the handler goes on only when `RemoveAsync` reports it deleted the key — Redis tells exactly one caller that (#450). The key is spent before any other check, so a refused exchange cannot retry it. The handler then re-checks the account against a freshly read `World` row — `Status == Active` and the same `AccessLevels.ForWorld(...).Allows(...)` rule — because the key lives five minutes and the account may have been banned or demoted since it was issued. The `inWorld` mutex is cleared only once every check has passed; a refused exchange leaves it to expire on its TTL.

**World access** is decided by `AccessLevels.ForWorld(world.AccessLevelRequired).Allows(account.AccessLevel)`, the same test in `CWorldListHandler` (what is listed) and `CWorldSelectHandler` (what can be entered). A Player world admits every player, Tournament and PTR included; a Tournament or PTR world admits holders of that flag plus staff; a staff-gated world admits its staff mask. Never compare a world's level with `<=`: `AccountAccessLevel` is `[Flags]`, and PTR (32) and Tournament (16) are numerically above Admin (4), which is how PTR accounts used to reach the Admin-only world.

**Key Redis patterns** (all string literals in `CacheKeys`):
- `world:{worldId}:keys:{base64}` — one-time world entry token (5 min TTL)
- `account:{accountId}:inWorld` — duplicate session mutex via `SETNX` (5 min TTL)
- `auth:account:{accountId}:mfa` — Redis Hash with MFA state (2 min TTL)
- `world:accounts:disconnect` — pub/sub: Auth→World, force-disconnect by accountId
- `auth:accounts:online` — pub/sub: login event (reserved, no subscriber yet)
- `world:{worldId}:select` — pub/sub: world-select event (reserved, for future sharding)

## World Simulation

`WorldServer` is an `IHostedService` that runs the tick loop at ~60 Hz (16.67ms intervals). On each tick it calls `World.Update(deltaTime)`, which ticks all active `MapInstance` objects via `IInstanceRegistry`.

**Two-pass tick architecture:** Each tick runs in two passes. Pass 1 — `WorldServer.Update` calls `connection.UpdateSession(deltaTime)` on every connected client, processing pre-character packets (`CMSG_PONG`, character list/select/create/delete) via `WorldSessionFilter`. Pass 2 — `World.Update → MapInstance.Update` calls `connection.UpdateMap(deltaTime)` on every in-map client, processing in-map packets (movement, attack, chat) via `MapSessionFilter`. `LockedQueue.Next(check)` uses peek-first semantics: a packet that fails the first-pass filter stays at the queue head and is picked up by the second pass. Both filter instances are stored on `WorldConnection` and reused each tick — no per-tick allocation.

- **MapInstance** (`Avalon.World/Instances/MapInstance.cs`) is the core simulation unit: manages entities in a flat tick context, runs `ChunkSpellSystem`, creature AI scripts, broadcasts state to clients. Implements `ISimulationContext`. Holds a single `MapNavigator` (combined navmesh) and the `ChunkLayout` that produced it.
- **InstanceRegistry** (`Avalon.World/Instances/InstanceRegistry.cs`) owns all live instances. `GetOrCreateTownInstanceAsync` returns a shared persistent instance; `GetOrCreateNormalInstanceAsync` returns per-player instances with 15-min re-entry windows. `ProcessExpiredInstances` cleans up expired normal instances. Both creators dispatch to `ChunkLayoutInstanceFactory.BuildAsync` for actual instance construction. A build runs on the thread pool and the instance is registered only once it finishes, so each creator keeps its build in flight in a `Lazy<Task>` keyed by map (towns) or by character and map (normals): a caller arriving mid-build awaits that build instead of starting a second copy (#442). The entry is removed in a `finally` after registration, so a failed build is retried by the next caller rather than cached.
- **ChunkLayoutInstanceFactory** (`Avalon.World/ChunkLayouts/IChunkLayoutInstanceFactory.cs`) — single entry point that builds both town and procedural instances. `IChunkLayoutSourceResolver` picks `PredefinedChunkLayoutSource` (DB-backed `MapChunkPlacement` rows for towns) or `ProceduralChunkLayoutSource` (RNG + `ProceduralMapConfig`) per `MapType`. `ChunkLayoutNavmeshBuilder` stitches chunk objs → DotRecast bake → `MapNavigator`. Server emits `SChunkLayoutPacket` to clients on instance enter; client `ClientMapNavigator` bakes the same navmesh from the same chunk objs (deterministic mirror) for prediction parity. See `docs/map-generation.md` for the full pipeline.
- **Creature placement has two paths, and both run from `BuildAsync`.** `CreaturePlacementService.PlaceAsync` rolls a `SpawnTable` against chunk spawn slots and is procedural-only, because it needs a `ProceduralMapConfig` and slots. `PlaceAuthoredAsync` reads `MapCreatureSpawn` rows — hand-placed creature, map, offset from the map's entry spawn point, facing — and runs for every layout kind; it is the only path that puts a creature in a town. Offsets, not world coordinates: a town's coordinates fall out of its `MapChunkPlacement` rows and the cell size. `OffsetY` only centres the navmesh search box, and `SampleGroundHeight` decides the real height. Both paths wrap each creature in its own try/catch — placement runs inside `MapInstance` construction, so a throw would make the map unenterable for everyone.
- **Town NPCs are creatures.** Templates 1-3 (Uriel, Borin Stoutbeard, Innkeeper) are placed on map 1 by `MapCreatureSpawns`, carry `Invulnerable = true`, and run `TownNpcScript`. They are visible but not interactive — there is no interaction handler and no dialogue anywhere; that is issue #431.
- **Every script resolvable by name must construct from `(creature, instance)` alone.** `AttachScript` builds it with `ActivatorUtilities.CreateInstance(sp, type, creature, instance)` — exactly two runtime arguments, anything else resolved from DI; a constructor wanting a plain `float` or array throws, and the throw is swallowed so the creature silently has no script. `ILoggerFactory` is fine (`AggroDefendScript` takes one). `AiScriptConstructibilityShould` builds every nameable script that way through the real World container. A component another script builds and chains with extra arguments (`CreatureRangeDetectorScript`, chained by `AggroDefendScript`) is marked `[ChainedScript]` (`Avalon.World.Public/Scripts`), and `ScriptManager` does not register it by name. `CreatureIdleScript` is gone (#437); use `TownNpcScript` for a passive creature.
- **Patrol paths are data, authored spawns only.** A `CreaturePath` (table `CreaturePaths`) holds ordered `CreaturePathPoint`s (`CreaturePathPoints`, keyed `(PathId, Sequence)`), each an offset from the map's entry spawn point plus a `WaitMs`; `MapCreatureSpawn.PathId` (nullable, shareable between spawns) names one. `PlaceAuthoredAsync` snaps each point to the navmesh like the spawn itself and sets `ICreature.PatrolPath` before attaching the script. `CreaturePatrolScript` walks it in a loop, standing at each point for its wait. The path does not choose the AI: the template's `ScriptName` still does, and only a script reading `PatrolPath` walks it. No path is seeded, and procedural spawns never get one.
- **ISimulationContext** — minimal contract used by AI scripts, spell system, and respawner instead of the old `IChunk`. Exposes `Locomotion` and `MeleeSlots` to scripts.
- **ICreatureLocomotion** (`Avalon.World.Public/Creatures/`) — the only thing that writes a creature's position. Scripts call `MoveTo` / `Stop` / `Teleport` / `HasArrived` and never touch `Position` themselves. `MapInstance` owns one, registers each creature in `AddCreature`, unregisters in `RemoveCreature`, and ticks it **after** the AI scripts — the scripts choose destinations, the locomotion consumes them, so the order matters and is pinned by a test. Two implementations in `Avalon.World/Creatures/Locomotion/`, selected by `GameConfiguration.CreatureLocomotion`: `WaypointLocomotion` (default; walks a navmesh path with no awareness of other agents) and `CrowdLocomotion` (DotRecast `DtCrowd`; agents steer around one another, and around players when `CrowdIncludesPlayers` is set). `Crowd` falls back to `Waypoint` with a warning on a map with no baked navmesh.
- **MeleeSlots** (`Avalon.World/Creatures/MeleeSlots.cs`) — hands each attacker a distinct standing position on a ring around its target so chasers surround rather than stack. Claims are keyed by target, world-fixed in angle (not facing-relative, which would churn every time the player turns), and handed out nearest the claimant's bearing. Attackers beyond `MeleeSlotCount` get no slot and stand off at attack range along their own bearing.
- **ChunkSpellSystem** manages the spell queue; deducts power cost on `QueueSpell`, ticks active `SpellScript` instances.
- **SpellScript** / **CreatureAiScript** — scriptable gameplay logic; hot-reloadable via `IScriptHotReloader`. `SpellScript.Clone()` has a virtual base implementation using `MemberwiseClone` (subclasses override for extra mutable state).
- **ICorpseRemover** / **CreatureCorpseRemover** — removes a dead creature's corpse from the instance after `ICreatureMetadata.BodyRemoveTimer`. Creatures do not respawn on a timer; the time-based respawn scheduler was removed deliberately.
- **Ground loot (#460)** lives in `Avalon.World/Loot/`. `CreatureTemplate.LootTableId` (nullable) names a `LootTable`; its `LootTableEntry` rows each drop an item or roll another table, exactly one of the two (a check constraint enforces it). Ungrouped entries roll on their own against `Chance` (a percentage); entries sharing a `GroupId` always drop exactly one of them, weighted by `Chance`, so a group that should not drop on every kill belongs in its own table, referenced at the chance it should drop with. `MinGold`/`MaxGold` are **copper** and make one pile. Tables load as the `/reload loot` area into `StaticData.Loot`, a `LootCatalog` that refuses a bad table (a reference cycle, a missing reference, both targets or neither, counts out of order or above 1000, a chance outside 0-100) with an error naming it, and keeps the rest. `MapInstance.OnCreatureKilled` rolls on the tick (`ILootRoller`), asks `ILootAllocator` for an owner (the only implementation gives everything to the instance owner, free for all after `GameConfiguration.LootGracePeriod`; groups will replace it), places each drop on a ring around the corpse snapped to the navmesh, keeps them in the instance's `GroundLootStore`, and broadcasts `SLootSpawnedPacket`. A character entering an instance gets the drops already there at the start of the instance's next tick, after the map transition packets. Drops are never persisted and never despawn on a timer: they leave when picked up, or when the instance is disposed. `CMSG_LOOT_PICKUP` (`LootPickupHandler`, with its `MapSessionFilter` entry) runs its checks in a fixed order in `LootPickup.TryPickUp` and answers with `SLootPickupResultPacket`; a dead character gets no answer.
- **EntityTrackingSystem** — tracks which entities are visible to which connections.
- **CreatureStatDeriver** (`Avalon.World/Creatures/`) — turns a creature template plus a rolled level into the stats it spawns with: `CreatureBaseStats` by level, scaled by the template's modifiers and its `CreatureRarity` via `CreatureRarityModifiers`. It lives on `StaticData` (`StaticData.CreatureStats`), and `CreatureSpawner.Spawn` reads it, together with the templates, off `world.Data.Creatures` once per call; before it existed every creature spawned as level 1 with 100 health whatever its template said. `CreatureTemplate.Exp` overrides the derived experience when set — `null` means derive.
- **`/reload <area>`** (`Avalon.World/Chat/ReloadCommand.cs`, game-master only) drives `IReferenceDataReloader` to apply changed dialogue, creature, ability, item, progression, or loot rows without a restart. Forward-only: `StaticData.PrepareAsync` reads the database and builds a whole `StaticDataPatch` off the tick thread; `ApplyOnNextTickAsync` queues it, and `World.Update` applies every queued patch at the top of `World.Update`, before the map pass and any instance ticks (the session pass — character create/select — runs earlier, in `WorldServer.Update`, so it sees a reload one tick later) — so nothing already spawned or already built is touched, only what gets built afterward. Each area is published as a single immutable `volatile` reference to its patch, because instance construction — and therefore `CreatureSpawner.Spawn` — runs on the thread pool, not the tick thread; a reader that needs more than one creature-area property together must take the `StaticData.Creatures` snapshot once rather than chain two property reads, or a reload landing in between could pair a new template with an old deriver. Maps and chunk layouts are excluded: live instances have already baked a navmesh from them, and the client mirrors that bake, so a change needs a restart. Spawn tables and town spawns need no reload of their own — `CreaturePlacementService` reads `ISpawnTableRepository`/`IMapCreatureSpawnRepository` straight from the database on every instance build, never from `StaticData`.
- **The seeded loot** (migration `SeedForestLoot`): hostile templates 4-10 each name their own table (2-8) and a copper range; the town NPCs (1-3) name none. Loot tables are generic, and shared tables are pools. Each creature table rolls a Health Potion and a Mana Potion on their own, then references the pools: table 1 "Forest common", table 9 "Forest weapons" at 2 % (a group of one weapon per class, items 5-8), table 10 "Forest scrolls" at 10 % (a group of three collectible scrolls, items 9-11, which do nothing when used yet), and table 11 "Forest armour" at 100 % (20 Uncommon pieces, items 12-31, one per class in each of five slots, each its own entry at 2 %). So a kill drops at most one weapon and at most one scroll. Item 4, the Rusted Sword, is left as it is (it may become a starting item) and is in no table. Chances rise with rarity. `SeedIntegrityShould` pins all of it.
- `CreatureTemplate.Invulnerable` **is** read — `CreatureSpawner` copies it onto the creature and `CombatService.ApplyDamageCore` returns early on it, ahead of `ResolveOrSpawn`, so an invulnerable target also creates no encounter and does not combat-tag its attacker.
- Deliberately still unread on `CreatureTemplate`: `ArmorModifier` (no mitigation step exists in `CombatService.ApplyDamage`), `ManaModifier` (creatures cannot cast), `RegenHealth`, `BaseAttackTime` (cadence is `CreatureCombatScript.AttackCooldown`), and `RespawnTimerSecs`. Do not assume any of them work.

**Two contracts worth knowing before touching creature movement or AI scripts:**
- The calling script owns the *moving* `MoveState` and `Speed`; a locomotion writes `MoveState` only when a creature comes to rest, setting `Idle` and zeroing `Velocity`. A locomotion that writes a moving value stomps the script every tick.
- `HasArrived` means "no further destination", which includes *no destination was reachable*. Treating it as "arrived successfully" turns a pathing failure into a silent success.

## Inventory, Gold and Character Saves

- **Everything a player owns lives in the Character DB**: `Character.Money` (the character's gold balance, in copper, a `ulong`), `ItemInstance`, and `CharacterInventory`, whose `ItemId` is a real FK to `ItemInstance` (cascade). `ItemInstance.CharacterId` is a real FK to `Characters` (cascade), so the hard delete in `CharacterDeleteHandler` takes the character's items with it, as it already did its slots. The World DB holds reference data only; `ItemInstance.TemplateId` points into it with no FK, and the REST API looks templates up with `IItemTemplateRepository.GetByIdsAsync`. Item ids come from `IItemIdAllocator` (`Guid.CreateVersion7()`), never from EF or the database (`ValueGeneratedNever`).
- **Memory is authoritative.** Change items only through `IInventoryService` and money only through `IWallet`, both from `ICharacterEconomy` in `Avalon.World/Inventory`. `ICharacterInventory` in World.Public is read-only apart from `Load`, which replaces a container's whole contents at character select and marks nothing. Tick thread only, all or nothing: adds fill existing Bag stacks, then the lowest free Bag slots, never Equipment or Bank; removal by template takes the highest Bag slot first. `IWallet.TryAddMoney` refuses past `Game:MaxMoney` and never wraps. A purchase is `CanAdd` + `CanSpend`, then `TrySpend` + `TryAdd`.
- Each successful change marks `CharacterEntity.SaveState` (`New`/`Changed`/`Removed` per item and per slot, plus a money-dirty flag) and `CharacterEntity.ClientChanges`. **Writing a `CharacterInventoryContainer` directly, or calling `ICharacterInventory.Load` after select, skips both: the change is neither saved nor sent.** Character creation is not a save: `CharacterCreateHandler` writes the starting items and slots directly through `IItemInstanceRepository` and `ICharacterInventoryRepository`, because no entity exists yet.
- **Saves go through `ICharacterSaver`**: one Character-DB transaction (`ICharacterSaveRepository`) with the whole row plus only the non-`Unchanged` items and slots, as idempotent upserts and deletes. `CharacterSaver` chains saves per `CharacterId`, not per entity, so a relog's saves still queue behind the previous session's despawn save; `WhenIdle(id)` completes once every save queued for that character so far has finished, committed or failed, and `CharacterSelectHandler` waits on it (up to `SaveWaitLimit`, 5 s) before loading the character. **A select whose wait runs out reads nothing**: it logs a warning and cancels the select (`CancelSelect`), and the client can select again. Reading anyway would load the state from before the save still running, and the new session's first save would write it back over the committed one. `WhenAllIdle()` is the same for every character at once: `WorldServer.OnStoppingAsync` awaits it last, because a tick starts each despawn without waiting for it, so a despawn save begun on an earlier tick can still be writing when the host stops. That wait is bounded by the host's stop token and `WorldServer.SaveDrainLimit` (20 s), and logs an error if it gives up. Marks are acknowledged only when the commit's continuation runs on the tick thread; a failed save keeps them for the next one. The task a save returns never faults (it is `false` on failure), but the multi-character `Save(list)` throws `ArgumentException` synchronously, before queuing anything, if it names a character twice or one item under two characters.
- **One world session per account (#465, #474).** Two copies of a character would each hold their own inventory and money, and whichever saved last would write over the other, losing or duplicating items and gold; and the owner's rule is one character in the world per account. The auth server's `Online` flag cannot prevent it (a dropped auth connection clears it while the world session stays up), and the `inWorld` mutex only spans world select to key exchange, so the world server enforces it at select. `CharacterSelectHandler` asks `IWorldServer.SessionsOf(accountId, except)` for the account's other connections, which includes closed ones whose despawn the tick has not dequeued yet (a close enqueues its despawn before it leaves `Connections`). **Every one of them is kicked**, whatever it holds (the selected character, a different one, a select still under way, or nothing): the handler cancels its select if one is in progress, calls `World.DeSpawnPlayerAsync` for it right there on the tick, then `NotifyAndClose` with `DisconnectReason.DuplicateLogin` and "Your account has been logged in from another location.". The despawn runs in the handler rather than being left to the close, because a close is only despawned by a later tick, and a `WhenIdle` taken before then finds no logout save to wait on. `DeSpawnPlayerAsync` queues its save before its first await, so the save is already in the chain the new select waits on, and clears `connection.Character` in a `finally`, so the connection ends holding nothing on every path, throw or not, and the despawn the close queues later saves nothing. Its instance steps (lookup, `DropPlayerFromEncounter`, `RemoveCharacter`) are each contained and logged, and the save is queued whatever they did, so a throwing step costs neither the logout save nor the removal of the others; `MapInstance.RemoveCharacter` drops the character from its tick, broadcast and periodic-save membership before it runs the disconnect hooks, so a hook that throws cannot leave it live. Only a throw while taking the snapshot itself queues nothing. The kicked connection's continuations still run after it lets go: `EnterMapHandler` and `RespawnAtTownHandler` check that the connection still holds the character they started with and otherwise do nothing, and `ProcessContinuations` contains each callback, logging one that throws, so it cannot end the flush for the connections after it. The new select waits, before its first read, on one `Task.WhenAll` bounded by `SaveWaitLimit`: its own character's `WhenIdle`, the `WhenIdle` of every *different* character it kicked (taken after that character's despawn queued its save), and the select step each kicked mid-select connection still had in flight. If the wait runs out it reads nothing, as above. **A kicked select stops at its next step**: every step of the select chain goes through `CharacterSelectHandler.Step`, which records the step's task per connection (a `ConditionalWeakTable`, tick thread only) and runs the next step only if `OwnsSelect` holds, meaning the connection is still up, not closing (`IWorldConnection.IsClosing`, read-only, set the moment `Close` is called), and `SelectStartedTicks` still equals the value that select began with. A select from a closing connection is ignored at `Execute` too: a kicked connection stays `IsConnected` until its outbox flushes, so a select it had queued in the same tick would otherwise kick back the session that kicked it, leaving neither in the world. Cancelling the select (the kick, the chain's own give-up paths, or `CancelExpiredSelects`) or losing the connection therefore stops the chain before its row write, its inventory reads and its `SetPendingSpawn`; the one step already running cannot be called back, which is why the new select waits for it. Other accounts' sessions are never touched.
- The save triggers: entering an instance (`World.SpawnInInstance`, which writes `Online = true`), map entry (`EnterMapHandler`), periodic (`CharacterSaveScheduler`, ticked from `MapInstance.Update`, every `Game:CharacterSaveInterval`, default 5 min, with each character's first save staggered across the interval by its id), and despawn (`SaveOnDespawnAsync`, every logout and disconnect). The despawn snapshot is taken on the tick; a dead logout's move to the respawn town needs a database lookup, so it is applied to the copied row inside the chained write. That lookup reads the World DB while the save writes the Character DB, so `World.MoveToRespawnTownAsync` never lets a failed lookup fail the save: it logs and falls back to town 1's default spawn, or, when map template 1 is not loaded, keeps the map and position the character died at. The select-time row update is still a plain `ICharacterRepository.UpdateAsync`, because nothing is loaded yet. The multi-character `Save(list)` is the one-transaction save for exchanges that must not half-happen (auction house, trade, mail); nothing calls it yet.
- **Client:** `SInventorySnapshotPacket` carries `Money` at login. `SInventoryUpdatePacket` (`SMSG_INVENTORY_UPDATE`) carries absolute `{Container, Slot, Item?}` entries plus an optional `Money`, built at most once per tick per connection by `InventoryUpdateFlusher` in `WorldServer.Update`, after both passes. Bank slots are never sent. The only client-to-server packet that changes inventory is `CMSG_LOOT_PICKUP` (#460); the rest are still to come (#463), and nothing is bought or sold yet (#432).

## Chat Commands

Commands live in `Avalon.World/Chat/`. To add a new command:
1. Implement `ICommand` (has `Name`, `Aliases[]`, `ExecuteAsync`).
2. Register as `services.AddSingleton<ICommand, YourCommand>()` in `ServiceExtensions.AddWorldServices`.
3. `CommandDispatcher` resolves all `ICommand` registrations from DI automatically.

`ChatMessageHandler` routes `/`-prefixed messages to `ICommandDispatcher`; unknown commands reply with "Unknown command." Non-slash messages broadcast to the current `MapInstance`.

`ICommand.RequiredAccess` defaults to `AccessLevels.Player`, so a command that declares nothing stays runnable by any logged-in player — `Tournament` and `PTR` included: they are players with exactly the Player permission set, differing only in which worlds they may enter, and the API's Player policy lists them too; `ReloadCommand` is the one exception so far, declaring `AccessLevels.GameMaster`. `CommandDispatcher` checks `RequiredAccess.Allows(connection.AccessLevel)` — a mask test, not an ordinal one, because `AccountAccessLevel` is `[Flags]` and `Tournament`/`PTR` are not above `Admin`, so a `>=` comparison would let either through a `GameMaster` gate. A caller without access gets exactly "Unknown command.", the same as a caller who typed a command that doesn't exist — the dispatcher never reveals which commands are staff-only. `IWorldConnection.AccessLevel` has no setter: `Avalon.World.Public` is the future modding API, and a settable access level there would let a mod promote any player to GM. The server assigns it once, at character-select, through `IAccessLevelAssignable` in `Avalon.World`.

A command that throws is caught by `CommandDispatcher`, logged at Error, and still counts as dispatched — so nobody gets "Unknown command." for it. Only a caller holding the `GameMaster` or `Admin` flag is told, as `Command /<name> failed: <ExceptionType>.` — the type name only; the message and stack stay in the log. Everyone else, `Console` included, gets silence, because an error line would reveal that the command exists and what broke inside it. The catch has to live in the dispatcher: `WorldConnection.ProcessContinuations` logs a faulted continuation and drops its callback, so an exception that escapes leaves the caller with no reply at all.

## ValueObject Pattern

`ValueObject<TPrimitive>` (in `Avalon.Common`) wraps primitives like `AccountId`, `WorldId`, `CharacterId`. Fourteen of them, used across ~217 files.

**They live inside the server and stop at every boundary.** Each edge unwraps them explicitly rather than relying on automatic serialization:

| boundary | how |
|---|---|
| Database | EF `HasConversion`, ~42 registrations across the three DbContexts |
| Protobuf wire | packet contracts declare primitives; handlers pass `.Value` |
| REST JSON | DTOs declare primitives; mappers pass `.Value`. Enforced by `ApiContractShould` |

There is nothing on a value object making it serialize as its primitive automatically — no attribute, no global converter. `Avalon.Common.Converters.ValueObjectJsonConverterFactory` does it, but only for a `JsonSerializerOptions` that registers it, and its only caller is the item-catalog export, which serializes `ItemTemplate` entities directly. Serializing a value object without it yields `{"value":42}`.

The API deliberately registers neither that converter nor an OpenAPI schema transformer, because nothing on its surface is a value object. If a DTO ever needs to expose one, `ApiContractShould` fails and says so — that is the signal to register the converter and flatten the OpenAPI schema, not to delete the test.

## Testing Conventions

- Framework: **xUnit** + **NSubstitute** for mocks.
- Test files are named `<Subject>Should.cs`, methods follow `Should_<verb>_<condition>` or descriptive names.
- No integration test infrastructure (no real Redis/Postgres in unit tests) — all external dependencies are substituted.
- Auth handler tests construct handlers directly via `new CAuthHandler(...)` with `NullLoggerFactory` and `NSubstitute` fakes.

## Key Open TODOs

Tracked as GitHub issues; the `TODO-0NN` numbering below predates that and survives only in this file and in a few code comments (there is no `TODO.md` in the repo). High-priority open items:
- **TODO-007** — `AvalonAuthenticationHandler` bearer token validation is a hardcoded stub
- **TODO-017/018** — `AnimationId` missing from `SpellTemplate`/`SpellMetadata` (needs EF migration)
- **TODO-029** — World server depends on `AuthDbContext` via `AddAuthDatabase()` — should use Redis-backed `IAccountSessionService` instead
