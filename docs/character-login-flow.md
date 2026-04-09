# Character Login Flow

This document describes the full sequence from world-select to the player being in the world, including all open gaps.

Related TODOs: [TODO-024](todo.md#todo-024), [TODO-025](todo.md#todo-025), [TODO-026](todo.md#todo-026)

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
    │  CCharacterSelectPacket│                               │
    │───────────────────────>│                               │
    │                        │ CharacterRepository.GetById   │
    │                        │──────────────────────────────>│
    │                        │<──────────────────────────────│ Character
    │                        │                               │
    │                        │ [OnCharacterReceived]         │
    │                        │  Build CharacterEntity        │
    │                        │  Assign InstanceId (TODO-026) │
    │                        │  world.SpawnPlayer(connection)│
    │                        │                               │
    │  SCharacterSelectedPacket                              │
    │<───────────────────────│                               │
    │                        │                               │
    │                        │ CharacterRepository.UpdateAsync (online=true)
    │                        │──────────────────────────────>│
    │                        │                               │
    │                        │ InventoryRepository.GetByCharacterId
    │                        │──────────────────────────────>│
    │                        │<──────────────────────────────│ List<CharacterInventory>
    │                        │ [OnInventoryReceived]         │
    │                        │  Load into entity containers  │
    │  SInventoryPacket      │  Send to client (TODO-024)    │
    │<───────────────────────│                               │
    │                        │                               │
    │                        │ SpellRepository.GetCharacterSpells
    │                        │──────────────────────────────>│
    │                        │<──────────────────────────────│ List<CharacterSpell>
    │                        │ [OnSpellsReceived]            │
    │  SSpellListPacket      │  Resolve SpellMetadata        │
    │<───────────────────────│                               │
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
| `X, Y, Z`         | `character.X/Y/Z`                       |
| `Orientation`      | `character.Rotation`                    |
| `Running`          | `character.Running`                     |
| `Experience`       | `character.Experience`                  |
| `RequiredExperience`| `entity.RequiredExperience`            |
| `MapId`            | `character.Map`                         |
| `InstanceId`       | See [Instance ID section](#instance-id-todo-026) |

---

## Inventory On Login (TODO-024)

### Current State

`OnInventoryReceived` loads items into `entity[InventoryType.*]` containers but does nothing further. The client starts with an empty display regardless of what was saved.

### `SInventoryPacket` Definition

Define in `Avalon.Network.Packets`:

```csharp
public class SInventoryPacketItem
{
    public byte Container { get; set; }   // InventoryType enum value
    public byte Slot { get; set; }        // Slot index within container
    public uint ItemId { get; set; }      // Item template ID
    public uint Quantity { get; set; }    // Stack size
    public ushort Durability { get; set; }
}
```

### Send Logic

After all `.Load(...)` calls in `OnInventoryReceived`:

```csharp
var loginItems = items
    .Where(i => i.Container is InventoryType.Equipment or InventoryType.Bag)
    .Select(i => new SInventoryPacketItem
    {
        Container = (byte)i.Container,
        Slot      = (byte)i.Slot,
        ItemId    = i.ItemId,
        Quantity  = i.Quantity,
        Durability = i.Durability
    })
    .ToList();

connection.Send(SInventoryPacket.Create(loginItems, connection.CryptoSession.Encrypt));
```

**Note:** Bank items are **not** sent on login. They are sent when the player interacts with a banker NPC (future feature).

---

## Instance ID (TODO-026)

### Phase 1 — Main World (current)

All characters entering the default open world share one well-known instance GUID derived from the `WorldId`:

```csharp
// Deterministic GUID from WorldId
private static Guid GetMainWorldInstanceId(WorldId worldId)
    => new Guid(worldId.ToString("N").PadLeft(32, '0'));
```

Set in `OnCharacterReceived`:
```csharp
// TODO: Phase 2 — route through IInstanceManager for instanced content (dungeons, raids)
character.InstanceId = GetMainWorldInstanceId(world.Id).ToString();
```

### Phase 2 — Instanced Content (future)

When instanced zones are introduced, the flow becomes:

```
IInstanceManager.GetOrCreateInstanceAsync(character, mapId)
  ├── Open world map → return well-known main instance
  └── Instanced map  → check for existing group instance
                        OR create a new instance GUID
```

`MapInfo.InstanceId` in `SCharacterSelectedPacket` reflects the assigned instance.

---

## Movement Validation (TODO-025)

### Current Desync Detection

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

Define in `Avalon.Network.Packets`:

| Field       | Type    | Description                          |
|-------------|---------|--------------------------------------|
| `X`         | `float` | Server-authoritative X position      |
| `Y`         | `float` | Server-authoritative Y position      |
| `Z`         | `float` | Server-authoritative Z position      |
| `Timestamp` | `long`  | Server tick time for client reconciliation |

The client snaps its position to the correction on receipt.

---

## Test Strategy

| Scenario                                               | TODO |
|--------------------------------------------------------|------|
| Character with 5 equipment items → `SInventoryPacket` with 5 items | 024 |
| Empty inventory → packet sent with 0 items             | 024  |
| Bank items not in login packet                         | 024  |
| Two characters same world → same `InstanceId`          | 026  |
| Valid navmesh movement → client position accepted      | 025  |
| Movement through wall → correction packet sent         | 025  |
| `N` consecutive rejections → connection flagged        | 025  |
