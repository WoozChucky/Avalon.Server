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
    │  CCharacterSelectPacket│                               │
    │───────────────────────>│                               │
    │                        │ CharacterRepository.GetById   │
    │                        │──────────────────────────────>│
    │                        │<──────────────────────────────│ Character
    │                        │                               │
    │                        │ [OnCharacterReceived]         │
    │                        │  Build CharacterEntity        │
    │                        │  Assign InstanceId            │
    │                        │  world.SpawnInInstance(conn)  │
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
    │                        │  (inventory packet not yet sent to client)
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
| `X, Y, Z`         | `character.X/Y/Z` for procedural maps; **for towns, overridden with `instance.Layout.EntrySpawnWorldPos`** because persisted DB coords would land returning players outside the new chunk-composed town |
| `Orientation`      | `character.Rotation`                    |
| `MovementSpeed`    | `entity.GetMovementSpeed()` (base + future equipment/buff modifiers) |
| `Experience`       | `character.Experience`                  |
| `RequiredExperience`| `entity.RequiredExperience`            |
| `MapId`            | `character.Map`                         |
| `InstanceId`       | See [Instance ID section](#instance-id) |

Immediately after `SCharacterSelectedPacket`, the server emits `SChunkLayoutPacket` carrying the chunk layout (chunks, entry spawn, cell size, portal placements). The client's `AuthFlowOrchestrator` pre-subscribes to this packet BEFORE sending `CCharacterSelected` so the dispatcher's fire-and-forget delivery doesn't drop it during the scene-load gap; the captured packet stashes on `GameSession.InitialChunkLayout` for the in-scene `PlayerMovementPredictor` / `ClientMapNavigator` / `ChunkLayoutVisualizer` / `ChunkMarkerVisualizer` to consume on `Start`. See **[Map Generation](map-generation.md)** for the full layout pipeline.

---

## Inventory On Login

`OnInventoryReceived` loads items into `entity[InventoryType.*]` containers. The inventory packet is not yet sent to the client — the client starts with an empty display until this is implemented.

### Planned `SInventoryPacket`

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

After all `.Load(...)` calls, equipment and bag items will be sent to the client. Bank items are deferred until the player interacts with a banker NPC.

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

| Scenario                                               |
|--------------------------------------------------------|
| Two characters same world → same `InstanceId`         |
| Character with 5 equipment items → `SInventoryPacket` with 5 items |
| Empty inventory → packet sent with 0 items             |
| Bank items not in login packet                         |
| Valid navmesh movement → client position accepted      |
| Movement through wall → correction packet sent         |
| `N` consecutive rejections → connection flagged        |
