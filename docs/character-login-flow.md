# Character Login Flow

This document describes the full sequence from world admission to the player being in the world. How the client gets its join ticket from the REST API is in [Auth server: World entry](auth-server.md#world-entry).

---

## Full Login Sequence

```
Game Client              World Server                  Databases / API
    │                        │                               │
    │  TLS connect           │                               │
    │───────────────────────>│                               │
    │  CGameAdmissionPacket  │                               │
    │  (join ticket + key)   │ redeem the ticket, activate   │
    │───────────────────────>│ the session (off the tick) ──>│ API /internal/game
    │                        │<──────────────────────────────│ account, session, fence
    │  SGameAdmissionPacket  │                               │
    │<───────────────────────│                               │
    │  CWorldHandshakePacket │                               │
    │───────────────────────>│ version check                 │
    │  SWorldHandshakePacket │                               │
    │<───────────────────────│                               │
    │                        │                               │
    │  CCharacterListPacket  │                               │
    │───────────────────────>│                               │
    │                        │ CharacterRepository.FindByAccountAsync
    │                        │──────────────────────────────>│
    │                        │<──────────────────────────────│ List<Character>
    │  SCharacterListPacket  │                               │
    │<───────────────────────│                               │
    │                        │                               │
    │  CCharacterSelectedPacket                              │
    │───────────────────────>│ entry gate (maintenance),     │
    │                        │ kick the account's other      │
    │                        │ sessions, wait for saves      │
    │                        │ CharacterRepository.FindForGameplayAsync
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
    │                        │ CharacterRepository.UpdateForGameplayAsync (still offline;
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
    │                        │ quest rows (#433), then       │
    │                        │ CharacterIgnoreRepository.GetByCharacterIdAsync (#723)
    │                        │──────────────────────────────>│
    │                        │<──────────────────────────────│ ignored characters
    │  SIgnoreListPacket (the whole list, empty too)          │
    │<───────────────────────│                               │
    │                        │ CharacterAuraRepository.GetByCharacterIdAsync
    │                        │──────────────────────────────>│
    │                        │<──────────────────────────────│ saved auras (AuraRestore)
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

Immediately after `SCharacterSelectedPacket`, in the same continuation, the server sends `SChunkLayoutPacket` carrying the chunk layout (chunks, entry spawn, cell size, portal placements). A client must be ready for it as soon as it sends `CMSG_CHARACTER_SELECTED`, since it can arrive while the client is still loading the scene; the client composes the map and bakes its navmesh from it. See **[Map Generation](map-generation.md)** for the full layout pipeline.

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

The bank is loaded into its container but **not sent** at login: its slots reach the client only
while the bank is open at a banker ([inventory and saves](inventory-and-saves.md)).

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
    [ProtoMember(2)] public ulong Money { get; set; } // Character.Money, copper
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
- **`Money`** is the character's gold in copper. It is field 2, added after `Items`, so an older
  reader ignores it.

### `SInventoryUpdatePacket`

Sent at the end of any tick in which the character's equipment, bag or gold changed, at most once
per connection per tick (`InventoryUpdateFlusher`, called from `WorldServer.Update`). Values are
absolute, so a lost or duplicated packet cannot leave the client drifting:

```csharp
public class SInventoryUpdatePacket : Packet   // SMSG_INVENTORY_UPDATE, 0x3029
{
    [ProtoMember(1)] public InventorySlotUpdateDto[] Slots { get; set; } // null when only money changed
    [ProtoMember(2)] public ulong? Money { get; set; }                   // new balance; absent if unchanged
}

public class InventorySlotUpdateDto
{
    [ProtoMember(1)] public ushort Container { get; set; }
    [ProtoMember(2)] public ushort Slot { get; set; }
    [ProtoMember(3)] public ItemSlotDto? Item { get; set; } // absent: the slot is now empty
}
```

Several changes to one slot within a tick arrive as that slot's final value. Bank slots are sent
only while the bank is open.

---

## Character Creation

`CMSG_CHARACTER_CREATE` (`CCharacterCreatePacket`: `Name`, `Class`, `Gender`) gets exactly one
`SMSG_CHARACTER_CREATED` (`SCharacterCreatedPacket.Result`, field 1, an `SCharacterCreateResult`),
unless the connection is closed instead.

- **The name rule (#757):** 3 to 12 ASCII letters, `A`-`Z` and `a`-`z`, nothing else: no digits,
  spaces, punctuation or letters outside ASCII. It is checked exactly as sent, before anything is
  read, so a name with a leading or trailing space is refused, not trimmed. Shorter than 3 is
  `NameTooShort` (2), longer than 12 `NameTooLong` (3), and any other break `NameInvalid` (8).
- **Stored form:** the name is stored, and shown to everyone, with its first letter upper-case and
  the rest lower-case: `kAELA` becomes `Kaela`. The character list, select, chat, the party roster
  and the ignore list all show that form.
- **One name per world, whatever its case:** a name that differs from an existing character's only
  in case is `NameAlreadyExists` (1). Every lookup by name (`/w`, `/invite`, `/kick`, `/promote`,
  `/ignore`, `/unignore`) finds the character whatever case is typed.
- **Answer order:** the checks run in this order, and the first that fails answers:
  1. not logged in to the world, a character selected, being selected or leaving, or the
     connection closing: the connection is closed, with no answer;
  2. a gender the enum does not define: `InvalidClass` (4);
  3. the name rule: `NameTooShort` (2), `NameTooLong` (3) or `NameInvalid` (8);
  4. no gameplay authority for the connection's account (no admitted session): the connection is
     closed, with no answer;
  5. the class has no creation data or no level 1 stats: `InternalDatabaseError` (7);
  6. then one transaction under the account's gameplay guard
     (`ICharacterRepository.CreateForGameplayAsync`): the account already holds the maximum number of
     characters, `MaxCharactersReached` (5);
  7. a character already has the name, in any case (the unique index on the name key refuses the
     insert, whoever created it first): `NameAlreadyExists` (1);
  8. `Success` (0) once the character, its stats, abilities, starting items and their Bag slots are
     written, all in that transaction.

  Any other failure is `InternalDatabaseError` (7). A write the account's gameplay guard refuses (the
  session was replaced, or its lease ran out) is answered `InternalDatabaseError` too, and the
  connection is closed.
- **Other results:** `AlreadyInGame` (6) is defined but not sent today (a create while a character
  is selected closes the connection, step 1). `SCharacterCreateResult` is append-only.
- **Renames through the REST API** (`PATCH /world/{worldId}/character/{id}`, `name`, owner or admin)
  follow the same rule and stored form:
  - a name that breaks the rule, an empty or all-space `name` included, is a 400 validation error
    ("Character name must be 3 to 12 letters A-Z only."); leaving `name` out leaves the name alone;
  - a name another character holds in any case is a 400 "Name already taken", and so is a rename
    that loses the race to another character taking the name;
  - a character that is in the world is not renamed: 409 "Character is online; rename it while
    logged out.". Only a name that would change is refused; an admin patch of other fields, with the
    current name or none, still applies while the character is online.
  - a character deleted while the rename was under way is a 404, as for a missing character.

---

## Change Character (#663)

A player in the world returns to character selection on the **same** world connection, with no new
login, through one request and one answer:

```
Game Client                      World Server                         Character DB
    │  CCharacterLeavePacket         │                                     │
    │───────────────────────────────>│ out of encounter, conversation,     │
    │                                │ bank, shop and instance; the        │
    │                                │ connection holds no character       │
    │                                │ logout save (Online = false) ──────>│
    │                                │<────────────────────────── committed│
    │  SCharacterLeaveResultPacket   │                                     │
    │  (Result = Left)               │                                     │
    │<───────────────────────────────│                                     │
    │  CCharacterListPacket          │                                     │
    │───────────────────────────────>│  ... as at login                    │
```

- **Opcodes:** `CMSG_CHARACTER_LEAVE` (`0x2016`, no fields) and `SMSG_CHARACTER_LEAVE_RESULT`
  (`0x302B`, `Result`, field 1, a `CharacterLeaveResult`). Every leave gets exactly one answer, unless
  the connection is closed instead.
- **Results:** `Left` (1) once the character has left its instance and its logout save has
  committed; from then on the connection holds no character, and `CMSG_CHARACTER_LIST` and
  `CMSG_CHARACTER_SELECTED` are accepted again. `NoCharacter` (2) when it holds none; `Selecting` (3)
  while a select is under way or the selected character is waiting on the client's load report (leave
  once it is in the world); `AlreadyLeaving` (4) while a leave is under way. Refusals change nothing.
- **The leave is a logout.** `CharacterLeaveHandler` calls `IWorld.LeaveWorldAsync`, the path every
  logout and disconnect takes (`DeSpawnPlayerAsync`): it ends the conversation and closes the bank and
  shop, drops the character from its encounter, removes it from its instance (interrupting its casts
  and dropping its projectiles), and queues the logout save, which writes the row offline (a dead
  character is revived at its respawn town, one on a normal map moves to that map's town). The save
  runs through the character's save chain, so a select of the same character, on this connection or
  another, waits for it (`ICharacterSaver.WhenIdle`) and cannot read what it is still writing.
- **Until the answer:** `IWorldConnection.LeaveInProgress` is set, and the list, select, create and
  delete requests are refused as they are during a select: the connection is closed. The client waits
  for the answer. The target, last input sequence, respawn flag and conversation are cleared at the
  start; the locale, access level and time sync belong to the account and the connection, and stay.
- **Failure:** a logout save that fails, or a leave that throws, is never answered. The connection
  is closed with `DisconnectReason.CharacterSaveFailed` (5), since the character's last state is not
  known to be written. A connection that drops, or is kicked by another session of the account, while
  leaving is not answered either, and its close finds nothing left to save.
- **Ordering:** the leave is accepted by the session filter alone, whatever the connection holds, so
  it is handled in the session pass, before any instance ticks. In-map packets queued before it
  (movement, casts) are handled by the map pass first; in-map packets queued behind it were meant for
  the character that left, and the session pass drops them once the connection holds no character, so
  they cannot hold back the next character list.
- **Kept on purpose:** a leave is allowed in combat, as a logout is.

---

## Instance ID

Every `MapInstance` has its own `InstanceId`, a random GUID (`Guid.NewGuid()`), sent in `MapInfo.InstanceId` and
`SChunkLayoutPacket`. Login always lands in a town: the select resolves the character's town (walking a non-town
map back to its town) and joins the town's one shared, persistent instance
(`IInstanceRegistry.GetOrCreateTownInstanceAsync`), so every character in that town shares its id. Normal maps are
entered later through portals, into a per-character instance (`GetOrCreateNormalInstanceAsync`, with a 15-minute
re-entry window) or the party's ([instanced maps](instanced-maps.md)).

---

## Movement

Movement is server-authoritative. The client sends its input (`CPlayerInputPacket`: a sequence number, a direction
and a yaw); `PlayerInputHandler` drops input from a dead character and any sequence at or below the last one handled,
clamps the direction to unit length, steps the character at its movement speed for one tick, stops the step at walls
with the instance navmesh's `RaycastWalkable`, snaps it to the ground with `SampleGroundHeight`, and answers
`SPlayerStateAckPacket` with the authoritative position, velocity and yaw, tagged with the sequence it handled. The
client predicts from its own copy of the navmesh and reconciles against the acknowledgement.

---

## Maintenance Admission

The auth server's world list shows each world with a derived status. During the scheduled countdown, a ready world remains `Online` and non-Admins may still get a join ticket and enter it. At the stored UTC deadline its status becomes `Maintenance`, and the REST game admission neither lists it nor issues a join ticket for it to a non-Admin; a world without a fresh ready heartbeat is not offered to anyone (a join ticket request answers `WorldUnavailable`). The ticket's redemption checks the same again, and the world rechecks the persisted maintenance row before character selection and before releasing a pending spawn. These decisions expire after at most five seconds and, for players, no later than the deadline. This also covers a connection returned to character selection by the leave flow. Admins may enter a ready world after the deadline. A failed authoritative read refuses new entry.

Players already in-game receive System chat countdown warnings at enable, three minutes, one minute, thirty seconds, and each second from ten to zero. At zero the world stops dispatching queued and new packets from authenticated non-Admins, sends `DisconnectReason.Maintenance`, closes their connections, and completes the usual despawn and save. The process and its listener remain up for Admin verification. Disabling maintenance during the countdown cancels it.

---

## Test Coverage

| Scenario                                               | Test |
|--------------------------------------------------------|------|
| Two characters same world → same `InstanceId`         | see [instanced-maps.md](instanced-maps.md) |
| 2 equipment + 3 bag items → `SInventorySnapshotPacket` carries exactly 5, bank excluded, every field (`Container`, `Slot`, `ItemTemplateId`, `ItemInstanceId`, `Count`, `Durability`, `Flags`) asserted on at least one slot | `CharacterSelectHandlerShould.Send_Equipment_And_Bag_Items_In_The_Snapshot_But_Not_The_Bank` |
| Empty inventory → packet still sent, carrying the character's `Money`, `Items` empty (`null` on the wire, per protobuf-net's empty-repeated-field encoding) | `CharacterSelectHandlerShould.Send_the_money_in_the_snapshot_even_with_no_items` |
| Several changes to one slot in a tick → one `SInventoryUpdatePacket` entry at the final value; emptied slot → no `Item`; `Money` only when it changed; nothing sent when nothing changed | `InventoryUpdateFlusherShould` |
| The tick drains a character's inventory changes | `WorldServerBarrierTickShould.Send_a_characters_inventory_changes_on_the_tick_they_were_made` |
| Equipment, bag and bank all load into their own containers via the real select chain (`Load()`, not the packet) | `CharacterSelectChainShould.Load_Equipment_Bag_And_Bank_Into_Their_Containers` |
| Orphan row (no matching `ItemInstance`) → skipped, remaining items unaffected | `InventoryAssemblerShould.Skip_A_Row_Whose_Instance_Is_Missing` |
| Slot `>= MaxSlots` → dropped on `Load`, container size unaffected | `CharacterInventoryContainerShould.Refuse_A_Slot_Beyond_Its_Capacity` |
| `Load` replaces a container's whole contents; `Items`/`TryGet` return what went in | `CharacterInventoryContainerShould.Replace_Its_Whole_Contents_On_Reload` |
| Change Character: leave, answered `Left` only after the logout save commits, then list and select another character on the same connection | `CharacterLeaveShould.Leave_answer_Left_after_the_logout_save_then_list_and_select_on_the_same_connection` |
| A select of the leaving character from another session waits for the logout save | `CharacterLeaveShould.Hold_a_reselect_of_the_same_character_until_the_logout_save_commits` |
| Every leave result, and a failed save closing with `CharacterSaveFailed` | `CharacterLeaveHandlerShould`, `CharacterLeaveShould` |
| In-map packets before a leave run first; those behind it are dropped | `CharacterLeaveQueueShould` |
| Input stepped on the server, stopped at walls, acknowledged; dead or stale input dropped | `PlayerInputHandlerShould` |
