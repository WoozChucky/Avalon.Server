# Character Login Flow

This document describes the full sequence from world-select to the player being in the world.

---

## Full Login Sequence

```
Game Client              World Server                  Databases / Redis
    │                        │                               │
    │  TCP connect           │                               │
    │───────────────────────>│                               │
    │                        │ Validate world key            │
    │                        │──────────────────────────────>│ GET world:key
    │                        │<──────────────────────────────│ accountId
    │                        │ DEL world:key                 │
    │                        │──────────────────────────────>│
    │                        │                               │
    │  CCharacterListPacket  │                               │
    │───────────────────────>│                               │
    │                        │ CharacterRepository.GetByAccountId
    │                        │──────────────────────────────>│
    │                        │<──────────────────────────────│ List<Character>
    │  SCharacterListPacket  │                               │
    │<───────────────────────│                               │
    │                        │                               │
    │  CCharacterSelectedPacket                              │
    │───────────────────────>│                               │
    │                        │ CharacterRepository.FindByIdAndAccountAsync
    │                        │──────────────────────────────>│
    │                        │<──────────────────────────────│ Character
    │                        │                               │
    │                        │ [OnCharacterReceived]         │
    │                        │  Build CharacterEntity        │
    │                        │  Assign InstanceId            │
    │                        │  Hold as a pending spawn      │
    │                        │                               │
    │  SCharacterSelectedPacket                              │
    │<───────────────────────│                               │
    │  SChunkLayoutPacket    │                               │
    │<───────────────────────│                               │
    │                        │                               │
    │                        │ CharacterRepository.UpdateAsync (still offline;
    │                        │──────────────────────────────>│  SpawnInInstance sets Online later)
    │                        │                               │
    │                        │ CharacterInventoryRepository.GetByCharacterIdAsync
    │                        │──────────────────────────────>│  (CharacterDbContext)
    │                        │<──────────────────────────────│ List<CharacterInventory>
    │                        │ [OnInventoryReceived]         │
    │                        │                               │
    │                        │ ItemInstanceRepository.GetByCharacterIdAsync
    │                        │──────────────────────────────>│  (CharacterDbContext, beside
    │                        │<──────────────────────────────│   the slot rows)
    │                        │                               │
    │                        │ [OnItemInstancesReceived]     │
    │                        │  InventoryAssembler joins rows to instances,
    │                        │  skipping orphan rows and rows past MaxSlots
    │                        │  Load() all three containers (Equipment/Bag/Bank)
    │                        │                               │
    │  SInventorySnapshotPacket (Equipment + Bag only; the   │
    │<───────────────────────│  bank is loaded but not sent) │
    │                        │                               │
    │                        │ CharacterAbilityRepository.GetCharacterAbilitiesAsync
    │                        │──────────────────────────────>│
    │                        │<──────────────────────────────│ List<CharacterAbility>
    │                        │ [OnSpellsReceived]            │
    │  SCharacterAbilitiesPacket  Resolve AbilityMetadata     │
    │<───────────────────────│                               │
    │                        │                               │
    │  CCharacterLoadedPacket│                               │
    │───────────────────────>│  world.SpawnInInstance(conn)  │
    │                        │  (or the tick, once the       │
    │                        │   readiness barrier expires)  │
    │                        │                               │
    │  [In game — tick loop] │                               │
```

---

## `SCharacterSelectedPacket`

Sent immediately after the character entity is built and spawned. Contains:

| Field              | Source                                  |
|--------------------|-----------------------------------------|
| `CharacterId`      | `character.Id`                          |
| `Name`             | `character.Name`                        |
| `Level`            | `character.Level`                       |
| `Class`            | `(ushort)character.Class`               |
| `X, Y, Z`         | `character.X/Y/Z` for procedural maps; **for towns, overridden with `instance.Layout.EntrySpawnWorldPos`** because persisted DB coords would land returning players outside the new chunk-composed town |
| `Orientation`      | `character.Rotation`                    |
| `MovementSpeed`    | `entity.GetMovementSpeed()` (base + future equipment/buff modifiers) |
| `Experience`       | `character.Experience`                  |
| `RequiredExperience`| `entity.RequiredExperience`            |
| `MapId`            | `character.Map`                         |
| `InstanceId`       | See [Instance ID section](#instance-id) |

Immediately after `SCharacterSelectedPacket`, the server emits `SChunkLayoutPacket` carrying the chunk layout (chunks, entry spawn, cell size, portal placements). The client's `AuthFlowOrchestrator` pre-subscribes to this packet BEFORE sending `CCharacterSelected` so the dispatcher's fire-and-forget delivery doesn't drop it during the scene-load gap; the captured packet stashes on `GameSession.InitialChunkLayout` for the in-scene `PlayerMovementPredictor` / `ClientMapNavigator` / `ChunkLayoutVisualizer` / `ChunkMarkerVisualizer` to consume on `Start`. See **[Map Generation](map-generation.md)** for the full layout pipeline.

Selecting a character does not put it in the world. The entity is held out of its instance until
the client sends `CMSG_CHARACTER_LOADED`, or until the wait expires. See
**[Character Readiness Barrier](character-readiness-barrier.md)**.

---

## Inventory On Login

A character's inventory is two tables in the **Character database** (`CharacterDbContext`), joined
by a foreign key:

- `CharacterInventory` rows (`CharacterId, Container, Slot, ItemId`) say *where* an item sits.
  `ItemId` is a foreign key to `ItemInstance.Id` (cascade on delete).
- `ItemInstance` rows (`Id, TemplateId, CharacterId, Count, Durability, Charges, Flags, UpdatedAt`)
  say *what* it is. `Id` is allocated by the world server (`IItemIdAllocator`, a version-7 Guid).
  `TemplateId` points into the World database, which holds reference data only, so it has no
  foreign key.

`OnInventoryReceived` (in `CharacterSelectHandler`) fetches the rows via
`ICharacterInventoryRepository.GetByCharacterIdAsync`, then chains one more continuation —
`IItemInstanceRepository.GetByCharacterIdAsync` — before anything can be loaded or
sent. `OnItemInstancesReceived` correlates the two results (`InventoryAssembler`, keyed on
`ItemInstance.Id`), skipping a row whose instance is missing or whose slot is `>= MaxSlots`
(logged as a warning either way — a bad row must not corrupt or oversize a container). The result
loads all three containers: `entity[InventoryType.Equipment]`, `.Bag`, and `.Bank`.

The bank is loaded into its container but **never sent** — opening it is a separate interaction
the client does not yet have, so telling it about items it cannot show would leave it with nothing
useful to do with that information.

### `SInventorySnapshotPacket`

Sent from `OnItemInstancesReceived`, right after the containers are loaded and before the ability
continuation is queued. Carries equipment and bag only:

```csharp
// src/Shared/Avalon.Network.Packets/Character/SInventorySnapshotPacket.cs
[ProtoContract]
public class SInventorySnapshotPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_INVENTORY_SNAPSHOT; // 0x3028

    [ProtoMember(1)] public ItemSlotDto[] Items { get; set; }
}

[ProtoContract]
public class ItemSlotDto
{
    [ProtoMember(1)] public ushort Container      { get; set; } // InventoryType, numeric
    [ProtoMember(2)] public ushort Slot           { get; set; }
    [ProtoMember(3)] public ulong  ItemTemplateId { get; set; } // ItemTemplateId.Value
    [ProtoMember(4)] public Guid   ItemInstanceId { get; set; } // ItemInstanceId.Value, .bcl.Guid
    [ProtoMember(5)] public uint   Count          { get; set; }
    [ProtoMember(6)] public uint   Durability     { get; set; }
    [ProtoMember(7)] public uint   Flags          { get; set; } // ItemInstanceFlags, numeric
}
```

Notes for a client implementer:

- **Always sent, even when empty.** An absent packet and an empty one mean different things — a
  character with nothing carried still gets `SInventorySnapshotPacket` with zero items, not no
  packet at all.
- **`Items` can be `null` on an empty inventory.** protobuf-net writes nothing for a zero-length
  repeated field, so it round-trips as `null` rather than `[]` even though the property is declared
  non-nullable. Treat `Items == null` the same as "no items".
- **`ItemInstanceId` crosses as `.bcl.Guid`**, the same encoding the other three `InstanceId`
  fields on the wire already use — two fixed64s in .NET byte order, not sixteen bytes in RFC order.
- `ItemTemplateId` resolves to a name, icon, rarity, etc. via the vendored catalog — see
  `schema/items/item-catalog-v1.json` and `schema/items/item-schema-v1.json` (`docs/tooling.md`
  documents the exporter that produces both).

---

## Instance ID

All characters entering the default open world share one well-known instance GUID derived from the `WorldId`:

```csharp
// Deterministic GUID from WorldId
private static Guid GetMainWorldInstanceId(WorldId worldId)
    => new Guid(worldId.ToString("N").PadLeft(32, '0'));
```

Set in `OnCharacterReceived` using `IInstanceRegistry.GetOrCreateTownInstance` — see [instanced-maps.md](instanced-maps.md) for the full instance routing design.

### Instanced Content (future)

When instanced zones are introduced, `IInstanceRegistry.GetOrCreateNormalInstance` handles private per-player instances with a 15-minute re-entry window.

---

## Movement Validation

`CharacterMovementHandler` computes an interpolated server position and compares it to the client-reported position. A warning is logged if the distance difference exceeds `MaxDistanceDiffCheck (1.0f)`. The client position is always accepted.

### Planned Authoritative Validation

```
Client sends CPlayerMovementPacket
  │
  ├── Compute interpolatedPosition (existing)
  ├── Raycast from current position to clientSentPosition via IChunkNavigator
  │   ├── Navmesh allows path → accept client position
  │   └── Navmesh blocks path (collision)
  │         ├── Log anti-cheat event
  │         ├── connection.Character.Position = last valid server position
  │         └── Send SPositionCorrectionPacket (corrected position back to client)
  │
  └── If differenceDistances >= MaxDistanceDiffCheck (speed hack)
        ├── Log + send correction
        └── Increment per-connection rejection counter
              └── N consecutive rejections → flag / disconnect
```

### `SPositionCorrectionPacket`

| Field       | Type    | Description                          |
|-------------|---------|--------------------------------------|
| `X`         | `float` | Server-authoritative X position      |
| `Y`         | `float` | Server-authoritative Y position      |
| `Z`         | `float` | Server-authoritative Z position      |
| `Timestamp` | `long`  | Server tick time for client reconciliation |

---

## Test Coverage

| Scenario                                               | Test |
|--------------------------------------------------------|------|
| Two characters same world → same `InstanceId`         | see [instanced-maps.md](instanced-maps.md) |
| 2 equipment + 3 bag items → `SInventorySnapshotPacket` carries exactly 5, bank excluded, every field (`Container`, `Slot`, `ItemTemplateId`, `ItemInstanceId`, `Count`, `Durability`, `Flags`) asserted on at least one slot | `CharacterSelectHandlerShould.Send_Equipment_And_Bag_Items_In_The_Snapshot_But_Not_The_Bank` |
| Empty inventory → packet still sent, `Items` empty (`null` on the wire, per protobuf-net's empty-repeated-field encoding) | `CharacterSelectHandlerShould.Send_An_Empty_Snapshot_When_The_Character_Has_No_Items` |
| Equipment, bag and bank all load into their own containers via the real select chain (`Load()`, not the packet) | `CharacterSelectChainShould.Load_Equipment_Bag_And_Bank_Into_Their_Containers` |
| Orphan row (no matching `ItemInstance`) → skipped, remaining items unaffected | `InventoryAssemblerShould.Skip_A_Row_Whose_Instance_Is_Missing` |
| Slot `>= MaxSlots` → dropped on `Load`, container size unaffected | `CharacterInventoryContainerShould.Refuse_A_Slot_Beyond_Its_Capacity` |
| Container round trip: `Load` then `Items`/`TryGet` returns what went in | `CharacterInventoryContainerShould.Return_What_It_Was_Loaded_With` |
| Valid navmesh movement → client position accepted      |      |
| Movement through wall → correction packet sent         |      |
| `N` consecutive rejections → connection flagged        |      |

The last three rows describe the **planned** authoritative movement validation above and are not
yet implemented or tested; `CharacterMovementHandler` currently always accepts the client position.
