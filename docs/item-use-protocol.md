# Item Use Protocol

> **Audience:** client developers and LLMs handed the client codebase.
> **Status:** item use, V1. Amended in place when the item use protocol changes.

This is the client contract for using an item: a right-click on a Bag slot or on a worn item. Gear in the Bag is
equipped into the slot it is worn in; any other Bag item runs the item's script on the server (a potion, a scroll); a
worn item is taken off to the lowest free Bag slot. The wire schema is
`schema/avalon.proto` (re-exported with `tools/Avalon.Exporter`); field numbers below are the `[ProtoMember]`
numbers in it. What an item does when used is in the item catalog, `schema/items/item-catalog-v1.json`
(`useScript`, `useCastTimeMs`, `useCooldownMs`, `useCooldownGroup`, `useValue`; described by
`schema/items/item-schema-v1.json`). Server-side rules are described in [Item use (server design)](item-use.md).

## 1. Opcodes

| Opcode | Value | Direction | Packet |
|---|---|---|---|
| `CMSG_ITEM_USE` | `0x2082` | client to server | `CItemUsePacket` |
| `SMSG_ITEM_USE_RESULT` | `0x3091` | server to client | `SItemUseResultPacket` |

Both are encrypted, over TCP. The request is accepted only while the character is in the world (in a map).

## 2. The request

`CItemUsePacket`, sent on a right-click on a Bag slot or an Equipment slot:

| Field | Proto # | Type | Notes |
|---|---|---|---|
| `RequestId` | 1 | `uint32` | Chosen by the client; echoed in the answer, so the client can pair answers with requests. |
| `Container` | 2 | `InventoryType` | The container of the slot: `1`, the Bag (use or equip), or `0`, Equipment (take off). Anything else is answered `NotFound`. |
| `Slot` | 3 | `uint32` | The Bag slot, or the Equipment slot (0 Head to 10 OffHand, 11 and 12 Trinket, #1179; 13 is reserved and answered `NotFound`). A `byte` on the server (#921): a slot the container does not have is `NotFound`, and one past 255 fails the packet's decode, which closes the connection. |

Send one per click. Do not pre-gate on cooldowns, casts or health: the server checks everything and answers every
request.

## 3. The answer

Every request is answered with **exactly one** `SItemUseResultPacket`, to the requester only:

| Field | Proto # | Type | Notes |
|---|---|---|---|
| `RequestId` | 1 | `uint32` | The request's `RequestId`. |
| `Result` | 2 | `ItemUseResult` | Below. |
| `CooldownMs` | 3 | `uint32` | Only on `OnCooldown`: the milliseconds left, rounded up so it is at least 1. 0 otherwise. |
| `Message` | 4 | `string` | Only on `Refused`: the line to show the player. Absent otherwise. |

**When it comes.** An equip, a take-off and an instant use are answered at once. A use with a cast time is answered once, when
its cast ends: `Ok` when it completed, `Interrupted` when it ended early, `Refused` when the item's own check
refused at completion, `InternalError` when something failed. Meanwhile its `SMSG_UNIT_START_CAST` (section 6) is the
acknowledgement. A connection with no character gets no answer at all.

`ItemUseResult` is append-only:

| Value | Name | Meaning |
|---|---|---|
| 0 | `Unknown` | What a payload without the field decodes as. Never sent. |
| 1 | `Ok` | Equipped, taken off, or the item's effect ran. The change itself arrives separately (section 5). |
| 2 | `NotFound` | Neither a Bag nor an Equipment slot, a slot out of range or reserved, or an empty slot. |
| 3 | `NotUsable` | The item can be neither worn nor used (no script), its template is gone, or it is a stack of gear. |
| 4 | `Dead` | The character is dead. Nothing happened. |
| 5 | `OnCooldown` | The item's cooldown, or its group's, is running; `CooldownMs` is the time left. |
| 6 | `AlreadyCasting` | An ability cast is in progress. |
| 7 | `InCombat` | Reserved: nothing sends it yet. |
| 8 | `NotInCombat` | Reserved: nothing sends it yet. |
| 9 | `WrongEquipSlot` | The gear does not fit the slot it would go to. |
| 10 | `LevelTooLow` | The gear's required level is above the character's. |
| 11 | `WrongClass` | The gear is not for the character's class: its `allowedClasses` lists classes and not this one. An empty list allows every class (#1171). |
| 12 | `TargetFull` | No free Bag slot: for a worn item being taken off, or for the off-hand item a two-handed weapon would move out. |
| 13 | `Blocked` | A two-handed weapon and an off-hand item would both be worn. |
| 14 | `Interrupted` | The cast ended early (section 6). Nothing was spent and no cooldown started. |
| 15 | `Refused` | The item refused this use; `Message` says why. Show it. |
| 16 | `InternalError` | Something failed on the server; it is logged. Nothing was consumed and no cooldown started. |
| 17 | `Stunned` | The character is stunned (crowd control, [aura protocol](aura-protocol.md) section 10): it uses no item, gear included, until the stun ends. A silence refuses no item use. |
| 18 | `Locked` | The item is offered in a face-to-face trade ([trade protocol](trade-protocol.md) section 6): it is neither used nor worn until it leaves the trade. A cast whose item becomes locked before the bar ends is answered `Interrupted`. |
| 19 | `AttributesTooLow` | The gear requires more Strength, Agility or Intellect than the character has, as its last stats refresh derived them (gear worn and auras included, so the item a swap would take off still counts) (#1178). |
| 20 | `UniqueEquipped` | The gear's template is UniqueEquipped and another equipment slot already wears a copy of it: only one copy is worn at a time (#1179). Two copies of any other item may be worn. |

## 4. The checks, in order

1. **Dead**: `Dead`, before anything else; then **stunned**: `Stunned`.
2. **The slot**: a Bag or Equipment slot in range, not reserved, that holds an item, else `NotFound`.
3. **A worn item** (Container 0) is taken off (section 5), and nothing else below is asked: no template, cooldown or
   cast check, so an item whose template is gone can still be taken off. A take-off that passes its checks first
   ends a running item cast bar, which is answered `Interrupted`; a full Bag (`TargetFull`) leaves the bar running.
4. **A trade, then the template**: a Bag item offered in a face-to-face trade is `Locked` (#1175); an item whose
   template is gone is `NotUsable`.
5. **Gear** (an item whose template `slot` is one that is worn) is equipped (section 5), and nothing else below is
   asked: no cooldown, no cast check. An equip that passes its checks first ends a running item cast bar, which is
   answered `Interrupted`; a refused equip leaves the bar running.
6. **A script** (`useScript` in the item catalog): none is `NotUsable`.
7. **An ability cast in progress**: `AlreadyCasting`.
8. **Cooldowns**: the item's own and its group's (`OnCooldown`, with the longer of the two as `CooldownMs`).
9. **The script cannot be found or built, or the character's instance cannot be found**: `InternalError`.
10. **The item's own check**: a refusal is `Refused` with its line in `Message`.

**Cooldowns.** An item rests for `useCooldownMs` after a use succeeds; items with the same `useCooldownGroup`
share one rest (using one starts it for all of them). A refused, interrupted or failed use starts none. Cooldowns
live in memory on the server and reset when the character logs out; they are independent of the ability global
cooldown, so an item can be used right after an ability and the other way round.

## 5. What a use does

**Gear** is equipped exactly as dragging it onto the slot its type is worn in would be: the same rules (level,
class, attributes, two-handed weapons against the off hand) and the same refusals. An occupied slot swaps: the worn item goes
into the clicked Bag slot. A ring takes the empty finger, else the first finger. A two-handed weapon with an
off-hand item worn first moves that item to the lowest free Bag slot; with none free it is `TargetFull` and nothing
moves (the clicked slot does not count as free). Equipping refreshes the character's stats as a drag does
(a new `SMSG_CHARACTER_STATS` when a value it shows changed). An equip that passes its checks ends a running item
cast bar first: that use is answered `Interrupted`, then the equip `Ok`. A refused equip (`LevelTooLow`,
`WrongClass`, ...) changes nothing, the bar included.

**A worn item** (Container 0) is taken off exactly as dragging it to a Bag slot would be: it goes to the lowest free
Bag slot, and with none free it is `TargetFull` and nothing moves. Taking off is always allowed otherwise: either
hand (a two-handed weapon or an off-hand item needs nothing special), in combat (only `Dead` refuses), and an item
whose template is gone. It refreshes the character's stats as a drag does, and, like an equip, a take-off that
passes its checks ends a running item cast bar first: that use is answered `Interrupted`, then the take-off `Ok`.

**Any other item** runs its script. An item with no script is `NotUsable`.

**Where the change shows.** Every item change (an equip, a swap, a take-off, a potion drunk) arrives in that tick's
`SMSG_INVENTORY_UPDATE`, like any inventory change. Health restored arrives through state replication and as an
`SMSG_UNIT_HEALED` (healer and target the user, no `AbilityId`, `Result` None); power restored arrives through
state replication.

## 6. The cast bar

An item with `useCastTimeMs` shows a cast bar before its effect runs. It uses the ability cast packets:

- `SMSG_UNIT_START_CAST` (`SUnitStartCastPacket`): `Caster` 1, `CastTime` 2 (seconds), `AbilityId` 3 = 0,
  `CastId` 4, no `Footprint`, `ItemTemplateId` 6 = the item, `WorldTick` 7 = the world tick the bar started on
  (#1082: end the bar at that tick's server time plus `CastTime`, as for an ability).
- `SMSG_UNIT_FINISH_CAST` (`SUnitFinishCastPacket`) when it completes, or `SMSG_INTERRUPTED_CAST`
  (`SCharacterInterruptedCastPacket`) when it ends early, each with the same `CastId` (3) and `ItemTemplateId` (4),
  `AbilityId` 0.

All three go to every client near the caster. Draw a plain bar with the item's name or icon: there is no telegraph.
`CastId` comes from the same per-instance sequence as ability casts, so key and clear the bar by `Caster` and
`CastId` as for an ability (`docs/client-combat-protocol-migration.md`).

**A doodad's cast bar** (a doodad template with an interaction time, used through `CMSG_INTERACT`) is the same three
packets with the doodad variant: `SUnitStartCastPacket` with `AbilityId` 0, `ItemTemplateId` 0 and
`DoodadTemplateId` 8 = the doodad's template, the cast id, the cast time and `WorldTick`; the finish or the
interrupt with the same `CastId` and `DoodadTemplateId` 5, `AbilityId` and `ItemTemplateId` 0. Exactly one of
`AbilityId`, `ItemTemplateId` and `DoodadTemplateId` is set on a cast. Draw the bar with the doodad's name from the
doodad catalog. They go to the user and to the clients near it that have been told of the doodad, never to one the
doodad is hidden from. A client that loses the doodad from its view mid-cast (it despawned, a quest moved, or it
walked out of the doodad's reach) is sent no finish or interrupt for it, as one that walks out of range of an item
or ability cast is not: time a bar out by its cast time. It ends on the item cast's rules (moving, dying, leaving,
an accepted ability cast, an item use that passed its checks, gear included), and also when the character uses
another doodad; it ends interrupted too when, at its end, the doodad can no longer be used (another player spent it
first).

**What interrupts it:** the character moving, dying or leaving the instance; using another item (gear included, and
taking a worn item off); casting an ability the server accepts; using a doodad that passed its checks. Using another
item interrupts only once that use has passed its checks, so a click on an item on cooldown, on gear the character
cannot wear, or on a worn item with the Bag full, leaves the bar running. Taking damage does not interrupt it. When
the bar ends, the same item must still be in the same Bag slot: one moved out of it, or sold or destroyed whole,
meanwhile ends it as `Interrupted` (a stack that only lost part of its count is still the same item), and so does
an item offered in a trade meanwhile (#1175). The item's own
check is asked again at the end and can still refuse (`Refused`). An interrupted use spends nothing and starts no
cooldown.

## 7. Effects a client sees

Item scripts can do more than the seeded items do. What a client sees of each:

- **A teleport** arrives with the usual `SMSG_MAP_TRANSITION` and `SMSG_CHUNK_LAYOUT`, as a portal entry does. When
  its position had no walkable ground within 2 m horizontally and 4 m up or down, the client gets `SMSG_MAP_TRANSITION`
  with `Result` `NoWalkableGround` (7) and the character stays where it was; the item is already spent. A teleport
  into a party's instance can also be answered `MapNotFound` (the character left the party meanwhile) or
  `InstanceFull`, as a portal entry can. When the destination instance cannot be built, no `SMSG_MAP_TRANSITION`
  is sent at all, as for a portal entry or a respawn whose build fails, and the item is still spent.
- **A move under way** (a Town Portal Scroll's return, a teleport, a respawn, a party's return to town, or an earlier
  portal entry that has not arrived yet) refuses a portal entry meanwhile: `CMSG_ENTER_MAP` is answered `SMSG_MAP_TRANSITION` with `Result` `MoveInProgress` (8),
  and the character stays where it was until the move under way arrives.
- **A summoned creature** is an ordinary creature (added, updated, attacked, killed and looted as any other). If it
  is not killed it leaves view after its lifetime, 5 minutes unless the item says otherwise, on the instance's next
  tick once that time has passed; an instance nobody is in leaves it there until someone is.
- **A heal, power, damage, experience, quest progress, gold and items** arrive through the packets that carry them
  for any other source (state replication, `SMSG_UNIT_HEALED`, the damage packets, `SMSG_QUEST_UPDATE`,
  `SMSG_INVENTORY_UPDATE`).
- **A line** from an item is a system line, or a whisper-style line on the `Whisper` channel, to the user only.

## 8. The seeded items

| Item | Id | Cast | Cooldown | Effect | Refused |
|---|---|---|---|---|---|
| Health Potion | 1 | none | 30 s, group `potion` | Restores 30 % of maximum health. | At full health: "You are already at full health." |
| Greater Health Potion | 56 | none | 30 s, group `potion` | Restores 60 % of maximum health. | As the Health Potion. |
| Mana Potion | 2 | none | 30 s, group `potion` | Restores 30 % of maximum Mana or Energy. | For Fury or no pool: "You cannot drink this."; at full: "Your mana is already full." / "Your energy is already full." |
| Town Portal Scroll | 3 | 3 s | 30 s, its own | Moves its reader to the town of the current map, as a respawn resolves it. | In a town: "You are already in town."; while a return or another move is under way: "You are already on your way to town." |

Each consumes one item. Amounts are `floor(maximum × percent / 100)`, a fixed amount with no crit and no scaling,
never above the maximum. The three potions share the `potion` cooldown: drinking any of them rests all three. The
Town Portal Scroll can be read in combat; its reader leaves its fight on the way, and the scroll is spent once the
move starts. A reader who dies before arriving arrives in town revived, as a respawn would. While the return is
under way a portal entry is refused with `MoveInProgress` (section 7). The forest scrolls (items 9-11) have no use: they answer `NotUsable`.
