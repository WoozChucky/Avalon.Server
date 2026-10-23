# Character Login Flow

This document describes the full sequence from world admission to the player being in the world. How the client gets its join ticket from the REST API is in [World entry](api-authentication.md#world-entry).

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

## `SGameAdmissionPacket` refusals

The world's answer to `CGameAdmissionPacket` (`GameAdmissionHandler`) when it admits no one. Every refusal is followed
by the close of the connection, and binds nothing: no account, no lease, no character.

| `Result` | When |
|---|---|
| `InvalidRequest` (1) | Refused before the ticket is spent: no TLS, a ticket that is not one, a public key of the wrong length on a world that seals, a second admission on the connection. Also a key of the right length that is not a P-256 point, found after the redemption (its lease is revoked, `bad_public_key`) |
| `ServiceUnavailable` (3) | Try later. The identity service answered so (unreachable or overloaded, a session barrier still pending, or the world in maintenance or without a fresh heartbeat, `WORLD_UNAVAILABLE`), or the world could not redeem the ticket itself: its admission work queue was full, or the redemption threw (#1050). Also, before the ticket is redeemed, an admission whose source is over its budget (#1052): `World:Admission:MaxInFlightPerSource` (16) in flight, its IPv6 /56's `MaxInFlightPerIPv6Block` (32), or `MaxPerSourcePerMinute` (120) started in the last minute, its /56's `MaxPerIPv6BlockPerMinute` (480) |
| `UnsupportedClient` (4) | The client is too old (#1053): the version its game context states is below this world's `MinVersion` (`CLIENT_OUTDATED`) or below the API's floor (`UNSUPPORTED_PROTOCOL`). Final for this build: the client shows "update required" and does not retry. The REST join ticket answers the same case 426 with the version required ([world refusals](api-authentication.md#world-refusals)) |
| `WorldFull` (5) | The world's backstop (#967): the world is over its own `Game:PlayerLimit` plus margin and the account is not staff. Its session is ended as `backstop`, which frees its place. Sent only when identity's place ledger was wrong or lost, or the world's limit is below identity's: identity refuses a full world at the join ticket (503 `WORLD_FULL`), so a client meets this rarely; try later, as for `ServiceUnavailable` ([the backstop](world-player-limit.md#the-backstop)) |
| `AuthorizationRequired` (2) | Every other answer: a ticket expired, spent or for another world, a session revoked, replaced or in conflict, the game context revoked (`CONTEXT_REVOKED`), not allowed on this world (`WORLD_NOT_ALLOWED`: its application, or the account's access level), its account banned, deactivated or being consolidated, the character not the account's (`CHARACTER_NOT_FOUND`), or an answer from the identity service the world could not trust (`INVALID_ADMISSION`) |

**One source cannot fill the admission lane (#1052).** Any client that completes the world's TLS and sends a
well-formed join ticket, real or made up, takes a place in the lane and costs one redemption at the identity service,
so each source may have at most `World:Admission:MaxInFlightPerSource` (default 16) admissions in flight, its IPv6
/56 at most `MaxInFlightPerIPv6Block` (32), and it may start at most `MaxPerSourcePerMinute` (120) a minute, its /56
at most `MaxPerIPv6BlockPerMinute` (480)
([configuration](configuration-reference.md#world-admission-budget)). The source is the client's address as the world
resolves it (after a trusted proxy's PROXY v2 header, so only as trustworthy as the path to that proxy), an IPv4
address or an IPv6 /64. The check comes after the refusals that cost nothing (`InvalidRequest` above) and before the
redemption is queued; one over the budget is answered `ServiceUnavailable` and closed at once, on the receive path
that delivered it, counted under `world.admission.source_refusals` (`reason` `in_flight` or `rate`) with one Warning at
most every 10 s naming the source ([instrumentation](instrumentation.md#work-queue-refusals-and-unserved-admissions)).
Its slots (the source's, and its /56's) are given back exactly once: at once when the lane refuses the redemption,
otherwise when the redemption ends, however it ends (admitted, refused, faulted, cancelled). A connection that closes
first keeps them until then, since its redemption still runs. Sources listed in `World:Admission:ExemptSources` (a
load-test bot PC) are not counted. No value was added to the wire.

**An admission the world cannot redeem is answered, not dropped (#1050).** The world has at most 128 redemptions
outstanding, 32 of them running (`WorldDatabaseWork.Admission`); a reconnect storm past that is refused as it arrives,
never queued without bound. Such an admission is answered `ServiceUnavailable` and closed at once, on the receive path
that delivered it, as the refusals before a ticket is spent are; one whose redemption throws is answered the same way
on the tick, as one the identity service answered "try later" is. The full queue is counted under
`world.work.refusals` (lane `admission`) and the throw under `world.admission.failures`
([instrumentation](instrumentation.md#work-queue-refusals-and-unserved-admissions)): a full queue with one Warning at
most every 10 s, a throw with one Error at most every 10 s. Before, its tick continuation faulted: the client got no answer until the
15-second admission timeout closed it, and the world logged an Error with a stack for each. Nothing is left live: a
full queue never reached the identity service, and an admission client that throws after the redemption reserved a
session ends that session before it lets the fault through (a second fault in that end is dropped, and the session
runs out with its lease). A redemption that did return a lease the connection never published is still ended by the
connection's drain, which reads the redemption the connection tracks (`WorldConnection.TrackAdmission`). A full
admission queue holds back no live player: lease heartbeats run on a lane of their own
([game server admission](steam-authentication-workloads.md)).

Before #1050 a redemption answered `WORLD_UNAVAILABLE` was refused `AuthorizationRequired`. It covered a refusal no
retry changes too, the context's application not allowed on the world, which the redemption now answers
`WORLD_NOT_ALLOWED` (the REST join-ticket issue answered `WORLD_UNAVAILABLE` for it until #1053, and now answers 403
`WORLD_NOT_ALLOWED`). No value was added to the wire.

Before #1053 the admission client turned every refusal it did not know into `SERVICE_UNAVAILABLE`, so an account's
standing and a revoked context were answered "try later", and nothing answered `UnsupportedClient`. The
redemption now names the allocator's reason, and the client passes the client-version, standing, revoked-context and
character refusals through (`GameAdmissionClient`), which the world maps as the table says. No value was added to
the wire: `UnsupportedClient` was declared already.

**A character select the world cannot load is closed at once (#1050, owner decision).** Every read and the one write of
the select chain, the town lookup for a character saved outside a town included, and the world entry check before it,
run on the select's own work queue (the one the container registers, with the database lane's capacity: 256
outstanding). A step or an entry check it refuses because it is full cancels the select and closes the connection as
busy, with `SelectTimeout` and "The world is busy; try again", the answer the stalled-select sweep gives a select that
cannot finish, which such a select got 15 s later before; the client logs in and selects again. The final spawn's entry
check (on `WorldDatabaseWork.ThreadPool`) is answered the same way. The refusal is counted under
`world.work.refusals` (lane `select`, or `database` for the final spawn). It is safe at any step: nothing before the
pending spawn is live. The one write, the selected character's row (still offline), writes back the row as the select
read it, with the town it was moved to if it was elsewhere, and the next select reads and writes it again; the entity
is in no instance and held by nothing but the chain. An entry check that ran and failed (it threw, or took more than
5 s) still closes with `Maintenance` (#821): only a full queue, which never ran it, is the busy answer.

---

## `SWorldHandshakePacket`

The world's answer to `CWorldHandshakePacket(Version)`, the first packet after admission (`WorldHandshakeHandler`).
The client's version must be at least the world's `MinVersion` (the auth `Worlds` row), the world's only floor. The
oldest wire any client may speak is not the world's to judge (#949): the API's game sign-in and world allocation refuse a
client below `Application:GameWorkloads:MinClientVersion` before it is given a join ticket
([configuration reference](configuration-reference.md#rest-api-game-client-version)). That floor judges the version the
client stated at sign-in; the handshake's is a separate statement, compared with `MinVersion` alone (binding the two
would need the context's version carried in the redemption receipt). Since #1053 the API also compares the version
stated at sign-in with the world's `MinVersion` before it issues a join ticket (426 `CLIENT_OUTDATED`, and
`UnsupportedClient` at the admission), so this handshake refusal is the backstop for a client whose two statements
differ. Before #949 the world also held a
compiled-in floor of its own, 0.3.0, and required the higher of the two.

| Field | Type | Meaning |
|---|---|---|
| `AccountId` (1) | `int64`, optional | The admitted account, on an acceptance and on a refusal alike |
| `Verified` (2) | `bool` | True: accepted; character selection follows, and the world's first time-sync ping |
| `Refusal` (3) | `WorldHandshakeRefusal` | Why the handshake was refused: `None` (0) on an acceptance, `ClientVersionTooOld` (1) for a client older than the world accepts. Append-only: read a value the client does not name as a refusal it cannot explain |
| `MinVersion` (4) | `string`, optional | On `ClientVersionTooOld`, the oldest client version the world accepts: the world's `MinVersion`, written as the plain `major.minor.patch` it was compared as (a configured ` 01.00.0` goes out as `1.0.0`); absent otherwise |

**A client too old is told so, then closed.** A version below the world's `MinVersion`, and one that is missing or does
not parse as `major.minor.patch` (a client that does not speak this versioning), gets `Verified` false, `ClientVersionTooOld` and
the required version, and the world closes the connection after it. The close flushes the connection's outbox before
it drops the socket (`Connection.CloseAsync`, [graceful shutdown](networking-graceful-shutdown.md#what-a-closing-connection-reads)),
so the reply reaches the client first. The world logs the refusal once, at Information: the connection, the account,
the client's version as it parsed (or `unparsable`; never the string it sent) and the version required. A client shows
the player that the game must be updated, naming `MinVersion`, rather than a network error; it does not retry, since
the same build is refused again. Before this field a too-old client was closed with no reply and read it as an
unreachable server.

**Every other refusal closes with no reply**, as before: a handshake with no active admission, a second handshake on a
connection already accepted (whatever version it states), and a world whose own `MinVersion` does not parse: a server
misconfiguration, under which the world closes every handshake, since it has no version to tell the client.

An acceptance is unchanged on the wire: `Refusal` at `None` and `MinVersion` null are not written, so its bytes are
those of the two first fields alone (`schema/corpus/SWorldHandshakePacket.txt`, `WorldHandshakeHandlerShould`).

---

## `SCharacterSelectedPacket`

Sent immediately after the character entity is built and spawned. Contains:

| Field              | Source                                  |
|--------------------|-----------------------------------------|
| `CharacterId`      | `character.Id`                          |
| `Name`             | `character.Name`                        |
| `Level`            | `character.Level`                       |
| `Class`            | `character.Class` (`CharacterClass`)    |
| `X, Y, Z`         | `character.X/Y/Z` for procedural maps; **for towns, overridden with `instance.Layout.EntrySpawnWorldPos`** because persisted DB coords would land returning players outside the new chunk-composed town |
| `Orientation`      | `character.Rotation`                    |
| `MovementSpeed`    | `entity.GetMovementSpeed()` (base + future equipment/buff modifiers) |
| `Experience`       | `character.Experience`                  |
| `RequiredExperience`| `entity.RequiredExperience`            |
| `MapId`            | `character.Map`                         |
| `InstanceId`       | See [Instance ID section](#instance-id) |

Immediately after `SCharacterSelectedPacket`, in the same continuation, the server sends `SChunkLayoutPacket` carrying the chunk layout (chunks, entry spawn, cell size, portal placements, the solid doodads' obstacle boxes and the scenery ring's placements and piece names, fields 9 and 10, which the client draws and never bakes). A client must be ready for it as soon as it sends `CMSG_CHARACTER_SELECTED`, since it can arrive while the client is still loading the scene; the client composes the map and bakes its navmesh from it. See **[Map Generation](map-generation.md)** for the full layout pipeline.

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
    [ProtoMember(2)] public ulong Gold { get; set; } // Character.Gold
}

[ProtoContract]
public class ItemSlotDto
{
    [ProtoMember(1)] public InventoryType Container { get; set; } // 0 Equipment, 1 Bag, 2 Bank (an enum in the schema)
    [ProtoMember(2)] public byte   Slot           { get; set; } // a byte: no container has 255 slots (#921)
    [ProtoMember(3)] public ulong  ItemTemplateId { get; set; } // ItemTemplateId.Value
    [ProtoMember(4)] public Guid   ItemInstanceId { get; set; } // ItemInstanceId.Value, .bcl.Guid
    [ProtoMember(5)] public uint   Count          { get; set; }
    [ProtoMember(6)] public uint   Durability     { get; set; }
    [ProtoMember(7)] public uint   Flags          { get; set; } // ItemInstanceFlags, numeric
    [ProtoMember(8)] public ItemRarity Rarity { get; set; } // the copy's (#1179): Common, Magic, Rare, Unique
    [ProtoMember(9)] public uint ItemLevel { get; set; } // the copy's (#1179)
    [ProtoMember(10)] public List<ItemModifierDto>? Modifiers { get; set; } // in the order the item shows them; null for none
}

[ProtoContract]
public class ItemModifierDto
{
    [ProtoMember(1)] public uint ModifierId { get; set; } // the catalog modifier
    [ProtoMember(2)] public List<ItemModifierValueDto> Values { get; set; } // in the modifier's stat-line order
}

[ProtoContract]
public class ItemModifierValueDto
{
    [ProtoMember(1)] public ItemStat Stat { get; set; } // the server's stat numbers, member for member
    [ProtoMember(2)] public int Value { get; set; }
    [ProtoMember(3)] public int? Value2 { get; set; } // set exactly on an "adds X to Y" line, its high end
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
- `ItemTemplateId` resolves to a name, icon, etc. via the vendored catalog — see
  `schema/items/item-catalog-v1.json` and `schema/items/item-schema-v1.json` (`docs/tooling.md`
  documents the exporter that produces both).
- **`Gold`** is the character's gold. It is field 2, added after `Items`, so an older
  reader ignores it.

### `SInventoryUpdatePacket`

Sent at the end of any tick in which the character's equipment, bag or gold changed, at most once
per connection per tick (`InventoryUpdateFlusher`, called from `WorldServer.Update`). Values are
absolute, so a lost or duplicated packet cannot leave the client drifting:

```csharp
public class SInventoryUpdatePacket : Packet   // SMSG_INVENTORY_UPDATE, 0x3029
{
    [ProtoMember(1)] public InventorySlotUpdateDto[] Slots { get; set; } // null when only gold changed
    [ProtoMember(2)] public ulong? Gold { get; set; }                   // new balance; absent if unchanged
}

public class InventorySlotUpdateDto
{
    [ProtoMember(1)] public InventoryType Container { get; set; }
    [ProtoMember(2)] public byte Slot { get; set; }
    [ProtoMember(3)] public ItemSlotDto? Item { get; set; } // absent: the slot is now empty
}
```

Several changes to one slot within a tick arrive as that slot's final value. Bank slots are sent
only while the bank is open.

---

## Character Creation

`CMSG_CHARACTER_CREATE` (`CCharacterCreatePacket`: `Name`, `Class` a `CharacterClass`, `Gender` a
`CharacterGender`, both wire enums of `Avalon.Common` since #922) gets exactly one
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
  2. a class a character cannot have (`None`, or a number that is no member) or a gender the enum
     does not define: `InvalidClass` (4). Both enums are bytes, so a class or gender past 255 does
     not fit and fails the packet's decode, which closes the connection, with no answer;
  3. the name rule: `NameTooShort` (2), `NameTooLong` (3) or `NameInvalid` (8);
  4. no gameplay authority for the connection's account (no admitted session): the connection is
     closed, with no answer;
  5. the class has no creation data or no level 1 stats (a playable class the World data lacks):
     `InternalDatabaseError` (7);
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
- **No goodbye:** Change Character keeps the connection and the session, so the account keeps its place on the world;
  the client never sends `CMSG_LEAVE_WORLD` for it (below).

## Quitting (#967)

A player who quits on purpose, to the desktop or back to the launcher, sends `CMSG_LEAVE_WORLD` (0x201F, no payload),
then closes the connection. The world ends the session as a `logout`, which releases the account's place on the world
at once; a close without the goodbye (a crash, a lost connection) is a drop, and the place is held for a short grace so
the player can come back ([world player limit](world-player-limit.md#the-goodbye),
[packet protocol](networking-packet-protocol.md#the-goodbye-cmsg_leave_world)). The character is saved and despawned
as on any close; there is no answer to wait for. Change Character never sends it.

---

## Instance ID

Every `MapInstance` has its own `InstanceId`, a random GUID (`Guid.NewGuid()`), sent in `MapInfo.InstanceId` and
`SChunkLayoutPacket`. Login always lands in a town: the select resolves the character's town (walking a non-town
map back to its town) and joins the town's one shared, persistent instance
(`IInstanceRegistry.GetOrCreateTownInstanceAsync`), so every character in that town shares its id. Normal maps are
entered later through portals, into a per-character instance (`GetOrCreateNormalInstanceAsync`, with a 15-minute
re-entry window) or the party's ([instanced maps](instanced-maps.md)).

A character's instance id is saved as text on its row (`Character.InstanceId`). `CharacterEntity.InstanceId` keeps
the id it last wrote there beside that text and answers from it while the row still holds that text (#977): every
input packet and every tick's quest flush read it, and parsed the text each time before. Set it through the entity
(a transfer, a select, an arrival's save), which writes the row; a row written another way is parsed on each read
until the entity sets it again.

---

## Movement

Movement is server-authoritative. The client sends its input (`CPlayerInputPacket`: a sequence number, a direction
and a yaw); `PlayerInputHandler` drops input from a dead character and any sequence at or below the last one handled,
clamps the direction to unit length, steps the character at its movement speed for one tick, stops the step at walls
with the instance navmesh's `RaycastWalkable`, snaps it to the ground with `SampleGroundHeight`, and answers
`SPlayerStateAckPacket` with the authoritative position, velocity and yaw, tagged with the sequence it handled. The
client predicts from its own copy of the navmesh and reconciles against the acknowledgement.

Both queries start from the nearest navmesh polygon to a point (where the character stands, for the raycast; where
the step ends, at the character's height, for the ground). Since #978 the navigator looks for it first in a narrow box
around the point (`StandingPolygonSearch`): a character on the ground scores zero on the polygon under it, nothing
scores lower, and the first zero in the narrow box is the first in DotRecast's full 4 x 8 x 4 m box, so the answer is
the full search's to the bit, overlapping floors and polygon edges included; beside the mesh, or between floors, the
full box decides as before. Nothing is kept from one input to the next. Creatures' routes and lines of sight and
projectiles' steps use the same search. `GroundedMovementShould` compares the navigator's queries with DotRecast's own
full searches (`DetourNavigationReference`) on the town, a forest and overlapping floors.

---

## Maintenance Admission

The worlds API reports each world's derived status; identity's `POST /game/worlds` lists only worlds the game context may enter. During the scheduled countdown, a ready world remains `Online` and non-Admins may still get a join ticket and enter it. At the stored UTC deadline its status becomes `Maintenance`, and the REST game admission neither lists it nor issues a join ticket for it to a non-Admin; a world without a fresh ready heartbeat is not offered to anyone (a join ticket request answers `WorldUnavailable`). The ticket's redemption checks the same again, and the world rechecks the persisted maintenance row before character selection and before releasing a pending spawn. These decisions expire after at most five seconds and, for players, no later than the deadline. This also covers a connection returned to character selection by the leave flow. Admins may enter a ready world after the deadline. A failed authoritative read refuses new entry.

Players already in-game receive System chat countdown warnings at enable, three minutes, one minute, thirty seconds, and each second from ten to zero. At zero the world stops dispatching queued and new packets from authenticated non-Admins, sends `DisconnectReason.Maintenance`, closes their connections, and completes the usual despawn and save. The process and its listener remain up for Admin verification. Disabling maintenance during the countdown cancels it.

---

## Test Coverage

| Scenario                                               | Test |
|--------------------------------------------------------|------|
| Two characters same world → same `InstanceId`         | see [instanced-maps.md](instanced-maps.md) |
| 2 equipment + 3 bag items → `SInventorySnapshotPacket` carries exactly 5, bank excluded, every field (`Container`, `Slot`, `ItemTemplateId`, `ItemInstanceId`, `Count`, `Durability`, `Flags`) asserted on at least one slot | `CharacterSelectHandlerShould.Send_Equipment_And_Bag_Items_In_The_Snapshot_But_Not_The_Bank` |
| Empty inventory → packet still sent, carrying the character's `Gold`, `Items` empty (`null` on the wire, per protobuf-net's empty-repeated-field encoding) | `CharacterSelectHandlerShould.Send_the_gold_in_the_snapshot_even_with_no_items` |
| Several changes to one slot in a tick → one `SInventoryUpdatePacket` entry at the final value; emptied slot → no `Item`; `Gold` only when it changed; nothing sent when nothing changed | `InventoryUpdateFlusherShould` |
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
