# Trade Protocol

> **Audience:** client developers and LLMs handed the client codebase.
> **Status:** face-to-face trading, V1 (2026-10-22, #1175). Amended in place when the trade protocol changes.

This is the client contract for face-to-face trading: two players standing next to each other each put up to 15 Bag
items and an amount of gold into a shared window, both see both offers, both confirm, and the swap happens all at once
or not at all. The wire schema is `schema/avalon.proto` (re-exported with `tools/Avalon.Exporter`); field numbers below
are the `[ProtoMember]` numbers in it. Server-side rules are described in [Trading (server design)](trading.md).

**The client re-vendors the schema** (`schema/avalon.proto`, `schema/opcodes.json`) to get the opcodes, the packets
and the enum values below, and the new `Locked` values of three existing enums (section 6).

## 1. Opcodes

Client to server. Every one is accepted only while the character is in the world (in a map), and every one is answered
with exactly one `SMSG_TRADE_RESULT` (section 2).

| Opcode | Value | Packet | Fields |
|---|---|---|---|
| `CMSG_TRADE_REQUEST` | `0x20D0` | `CTradeRequestPacket` | 1 `TargetCharacterId` (uint32: the player right-clicked, by character id) |
| `CMSG_TRADE_RESPOND` | `0x20D1` | `CTradeRespondPacket` | 1 `Accept` (bool; false declines) |
| `CMSG_TRADE_OFFER_ITEM` | `0x20D2` | `CTradeOfferItemPacket` | 1 `BagSlot` (uint32, a Bag slot, 0-29), 2 `TradeSlot` (uint32, a free trade slot, 0-14) |
| `CMSG_TRADE_REMOVE_ITEM` | `0x20D3` | `CTradeRemoveItemPacket` | 1 `TradeSlot` (uint32, 0-14) |
| `CMSG_TRADE_SET_GOLD` | `0x20D4` | `CTradeSetGoldPacket` | 1 `Gold` (uint64, 0 up to the balance) |
| `CMSG_TRADE_CONFIRM` | `0x20D5` | `CTradeConfirmPacket` | 1 `Revision` (uint32, the `Revision` of the `SMSG_TRADE_STATE` the player is looking at) |
| `CMSG_TRADE_CANCEL` | `0x20D6` | `CTradeCancelPacket` | none (the window's X is the same as Cancel) |

`BagSlot` and `TradeSlot` are bytes on the server: a value past 255 fails the packet's decode and closes the
connection, as any payload that cannot be read does.

Server to client.

| Opcode | Value | Packet | Fields |
|---|---|---|---|
| `SMSG_TRADE_REQUEST` | `0x30E0` | `STradeRequestPacket` | 1 `CharacterId` (uint32, the player asking), 2 `Name`, 3 `Class` (`CharacterClass`), 4 `Level`, 5 `ExpiresInMs` (how long the request stays open, 30 000 by default) |
| `SMSG_TRADE_OPENED` | `0x30E1` | `STradeOpenedPacket` | 1 `TradeId` (uint32), 2 `PartnerCharacterId` (uint32), 3 `PartnerName` |
| `SMSG_TRADE_STATE` | `0x30E2` | `STradeStatePacket` | 1 `TradeId`, 2 `Revision` (uint32), 3 `Completing` (bool), 4 `You` (`TradeSideDto`), 5 `Partner` (`TradeSideDto`) |
| `SMSG_TRADE_CLOSED` | `0x30E3` | `STradeClosedPacket` | 1 `TradeId`, 2 `Result` (`TradeResult`: `Ok` when the trade completed, otherwise why it ended) |
| `SMSG_TRADE_RESULT` | `0x30E4` | `STradeResultPacket` | 1 `Result` (`TradeResult`), 2 `Name` (optional: the other player the answer is about) |

`TradeSideDto`: 1 `Gold` (uint64, the gold this side offers), 2 `Confirmed` (bool, whether this side confirmed this
revision), 3 `Items` (repeated `TradeItemDto`, in trade-slot order, only the occupied slots).

`TradeItemDto`: 1 `TradeSlot` (uint32, 0-14), 2 `ItemTemplateId` (uint64), 3 `ItemInstanceId` (`.bcl.Guid`, the item
instance, as everywhere else an instance id crosses), 4 `Count` (uint32: the whole stack is offered), 5 `Details`
(`ItemDetailsDto`, always set: the item's rarity, item level and modifiers, as an inventory slot carries them, #1179).

`TradeId` is numbered from 1 each time the world server starts; it names one trade window for as long as it is open.

## 2. Answers

Every request is answered with exactly one `SMSG_TRADE_RESULT`. It arrives after anything the request sent itself (the
target's `SMSG_TRADE_REQUEST`, the opened trade and its first state, a new state), so a client can treat it as the end
of the request. `TradeResult` is append-only; 0 is what a payload without the field decodes as. The same enum says why
a trade closed (`SMSG_TRADE_CLOSED.Result`) and is sent unasked (below). Every value is from the point of view of the
player it is sent to: "this player" is the recipient, "the other player" the partner or the other side of the request.

| Value | Name | Meaning |
|---|---|---|
| 0 | `Unknown` | Never sent; a payload without the field. |
| 1 | `Ok` | Done: the request was applied (a decline is `Ok` too), or, on `SMSG_TRADE_CLOSED`, the trade completed. |
| 2 | `NotFound` | The requested player is not online in this player's map instance; also an accept whose requester has left it. |
| 3 | `Self` | A player cannot trade with themselves. |
| 4 | `TooFar` | The other player is beyond 5 m for a request, or beyond 6 m for an accept. |
| 5 | `TargetBusy` | The other player is already trading or holds an open request (sent or received). |
| 6 | `Busy` | This player is already trading or holds an open request. |
| 7 | `InCombat` | This player is in combat. |
| 8 | `TargetInCombat` | The other player is in combat. |
| 9 | `Dead` | This player is dead. |
| 10 | `TargetDead` | The other player is dead. |
| 11 | `NpcWindowOpen` | This player has a shop or the bank open. |
| 12 | `TooFast` | Too many trade requests too quickly: `Game:TradeRequestsPerMinute` (30) in any 60 s. Nothing was sent to the target. |
| 13 | `NoRequest` | An accept or decline with no open request to answer (it expired, was withdrawn, or never reached this player). |
| 14 | `Declined` | The other player declined this player's request (unasked). |
| 15 | `Expired` | A request ended unanswered: it ran out (30 s), or either player went offline or changed instance (unasked). |
| 16 | `NotTrading` | An offer, a remove, a gold change, a confirm or a cancel with no open trade. |
| 17 | `InvalidSlot` | A trade slot past 14, or a Bag slot the Bag does not have. |
| 18 | `ItemNotFound` | The named Bag slot is empty, or (a remove) the named trade slot is empty. |
| 19 | `NotTradeable` | The item is NoTrade, a quest item, or its template no longer exists. |
| 20 | `AlreadyOffered` | That item is already in the trade. |
| 21 | `TradeSlotTaken` | The trade slot already holds an item: remove it first. |
| 22 | `NotEnoughGold` | The gold offered is more than this player's balance. |
| 23 | `StaleOffer` | A confirm for a revision that is no longer current: the offer changed since the player saw it. A fresh `SMSG_TRADE_STATE` precedes it. |
| 24 | `Completing` | Both confirmed and the trade is being saved: nothing about it can change, and it cannot be cancelled. |
| 25 | `Cancelled` | This player cancelled the trade (on `SMSG_TRADE_CLOSED`). |
| 26 | `CancelledByPartner` | The other player cancelled the trade. |
| 27 | `OutOfRange` | The two players moved more than 6 m apart. |
| 28 | `EnteredCombat` | A player entered combat. |
| 29 | `Died` | A player died. |
| 30 | `Left` | A player left the map, the instance or the world. |
| 31 | `NpcWindowOpened` | A player opened a shop or the bank. |
| 32 | `BagFull` | This player's Bag would not have room for everything received, once this player's own offered items leave. |
| 33 | `PartnerBagFull` | The other player's Bag would not have room for everything they receive. |
| 34 | `GoldCapReached` | The trade would take this player's balance past the gold cap. |
| 35 | `PartnerGoldCapReached` | The trade would take the other player's balance past the gold cap. |
| 36 | `UniqueAlreadyOwned` | This player would own two copies of a Unique item. |
| 37 | `PartnerUniqueAlreadyOwned` | The other player would own two copies of a Unique item. |
| 38 | `OfferInvalid` | An offered item is no longer where it was offered or no longer tradeable, or a giver lacks the gold offered. |
| 39 | `SaveFailed` | The trade's save was refused or failed: nothing moved (on `SMSG_TRADE_CLOSED`). |
| 40 | `Error` | The server failed while handling the request; nothing is known to have changed. Also a trade whose save could not even start (on `SMSG_TRADE_CLOSED`). |
| 41 | `TargetNpcWindowOpen` | An accept while the requester has a shop or the bank open: the request stays open until it closes. |

`Name` on an answer: the requested player's name on the answer to `CMSG_TRADE_REQUEST` whenever that player is online
(whatever the result); none on the answers to the other requests. On an unasked result it names the other player (see
below).

**Unasked results.** Some `SMSG_TRADE_RESULT`s arrive without a request:
- `Declined` (with the target's name) to the sender when the target declines.
- `Expired` to the sender (with the target's name) and to the target (with the sender's name) when a request runs out
  unanswered, or when either goes offline (the one going offline is told nothing) or changes instance (both are told).
- On a completion that fails its checks (section 4.4): the failing player gets the reason (`BagFull`, `GoldCapReached`,
  `UniqueAlreadyOwned`, `OfferInvalid`) with no `Name`, and the other player its `Partner` form (`PartnerBagFull`,
  `PartnerGoldCapReached`, `PartnerUniqueAlreadyOwned`; `OfferInvalid` as it is) with the failing player's name.

**A request to a player who ignores you** (the ignore list, [party protocol](party-protocol.md) section 5) is answered
`Ok` like any other, but the target is never sent `SMSG_TRADE_REQUEST` and cannot accept it. It stays open for the
sender (a second request is `Busy`) and ends with `Expired` to the sender only, when it runs out, or when the target
goes offline or changes instance. The refusals before it are checked first, exactly as for anyone.

## 3. State

`SMSG_TRADE_STATE` is the whole trade, every time, as the recipient sees it: `You` is the recipient's own side,
`Partner` the other player's. A client replaces what it held and redraws. It goes to both players when the trade
opens (right after `SMSG_TRADE_OPENED`), on every change to either offer, on every confirm, and when the trade starts
completing; and to one player alone when its confirm named an old revision.

- `Revision` is 1 when the trade opens and goes up by one on every change to either offer (an item offered or
  removed, the gold changed). A confirm alone does not move it.
- Every change clears both `Confirmed` flags. A change that changes nothing (setting the gold already set) sends no
  state and clears nothing.
- `Completing` true: both confirmed and the trade is being saved. Show it as completing, with the buttons doing
  nothing: every edit, confirm and cancel is refused `Completing`. It ends with `SMSG_TRADE_CLOSED`.
- An offered item stays in its owner's Bag, shown as locked (section 6), until the trade completes or it is removed
  from the trade. Offered gold stays in the balance but cannot be spent meanwhile.

## 4. Exchanges

### 4.1 Request and accept

```
A -> CMSG_TRADE_REQUEST { TargetCharacterId = B }
B <- SMSG_TRADE_REQUEST { CharacterId = A, Name, Class, Level, ExpiresInMs = 30000 }
A <- SMSG_TRADE_RESULT  { Ok, Name = B }

B -> CMSG_TRADE_RESPOND { Accept = true }
A <- SMSG_TRADE_OPENED  { TradeId = 7, PartnerCharacterId = B, PartnerName = B }
B <- SMSG_TRADE_OPENED  { TradeId = 7, PartnerCharacterId = A, PartnerName = A }
A <- SMSG_TRADE_STATE   { TradeId = 7, Revision = 1, You = { 0 gold }, Partner = { 0 gold } }
B <- SMSG_TRADE_STATE   { TradeId = 7, Revision = 1, ... }
B <- SMSG_TRADE_RESULT  { Ok }
```

- A request needs both players in the same map instance, alive, out of combat, with no shop or bank open (the sender),
  neither trading nor holding another request, and the target within 5 m. A refused one is answered with the reason,
  and nothing reaches the target.
- An accept needs the same, with the two within 6 m. A refused accept is answered with the reason, from the accepter's
  side (the requester's shop or bank window is `TargetNpcWindowOpen`), and **leaves the request open** until it
  expires, so the player can step closer, close a shop, and accept again.
- A decline: `SMSG_TRADE_RESULT { Ok }` to the target, `{ Declined, Name = B }` to the sender.
- Unanswered for 30 s: `{ Expired }` to both, each naming the other.

### 4.2 An edit, and its state to both

```
A -> CMSG_TRADE_OFFER_ITEM { BagSlot = 4, TradeSlot = 0 }
A <- SMSG_TRADE_STATE  { Revision = 2, You = { Items = [ { 0, template 33, instance, 1 } ] }, Partner = { ... } }
B <- SMSG_TRADE_STATE  { Revision = 2, You = { ... }, Partner = { Items = [ { 0, template 33, instance, 1 } ] } }
A <- SMSG_TRADE_RESULT { Ok }
```

The same for `CMSG_TRADE_REMOVE_ITEM` and `CMSG_TRADE_SET_GOLD`. A refused edit sends no state: only the
`SMSG_TRADE_RESULT` with the reason.

### 4.3 Confirm with a revision

```
A -> CMSG_TRADE_CONFIRM { Revision = 2 }
A <- SMSG_TRADE_STATE  { Revision = 2, You = { Confirmed = true }, Partner = { Confirmed = false } }
B <- SMSG_TRADE_STATE  { Revision = 2, You = { Confirmed = false }, Partner = { Confirmed = true } }
A <- SMSG_TRADE_RESULT { Ok }
```

A confirm approves exactly the offer at that revision, so a last-second change can never be approved unseen: if B
changed anything after A last looked, A's confirm names an old revision, and A gets the current state (to A alone)
followed by `{ StaleOffer }`. Confirming again when already confirmed is `Ok` and sends nothing.

### 4.4 A completion that fails

The second confirm is first shown to both as a state with both `Confirmed` set. Then the server checks the two
players (online, alive, out of combat, no shop or bank open, within 6 m) and then the offers. A failed player check
closes the trade (4.6) with that reason (`Left`, `Died`, `EnteredCombat`, `NpcWindowOpened`, `OutOfRange`). A failed
offer check keeps the window open:

```
B -> CMSG_TRADE_CONFIRM { Revision = 2 }
A <- SMSG_TRADE_STATE  { Revision = 2, both Confirmed = true }
B <- SMSG_TRADE_STATE  { Revision = 2, both Confirmed = true }
A <- SMSG_TRADE_RESULT { BagFull }                   // A is the side that failed
B <- SMSG_TRADE_RESULT { PartnerBagFull, Name = A }
A <- SMSG_TRADE_STATE  { Revision = 3, both Confirmed = false }
B <- SMSG_TRADE_STATE  { Revision = 3, both Confirmed = false }
B <- SMSG_TRADE_RESULT { Ok }                        // the answer to B's confirm
```

Nothing moved; both confirmations are cleared and the revision has moved on, so the players fix the offer (free Bag
space, a lower gold amount, a different item) and confirm again.

The confirm itself is answered `Ok` whatever followed it: `Ok` means the confirm was taken, never that the trade
completed. When a player check closes the trade, the second confirmer gets `SMSG_TRADE_CLOSED { reason }` and then
`SMSG_TRADE_RESULT { Ok }`; likewise after `SMSG_TRADE_CLOSED { Error }` from a save that could not start. Only
`SMSG_TRADE_CLOSED { Ok }` reports a completed trade.

### 4.5 Completing

```
B -> CMSG_TRADE_CONFIRM { Revision = 2 }
A <- SMSG_TRADE_STATE  { Revision = 2, both Confirmed = true }
B <- SMSG_TRADE_STATE  { Revision = 2, both Confirmed = true }
A <- SMSG_TRADE_STATE  { Revision = 2, Completing = true, both Confirmed = true }
B <- SMSG_TRADE_STATE  { Revision = 2, Completing = true, both Confirmed = true }
B <- SMSG_TRADE_RESULT { Ok }
   ... the trade is saved (usually milliseconds, possibly longer) ...
A <- SMSG_TRADE_CLOSED { TradeId = 7, Ok }
B <- SMSG_TRADE_CLOSED { TradeId = 7, Ok }
A <- SMSG_INVENTORY_UPDATE { the items and gold that left and arrived }
B <- SMSG_INVENTORY_UPDATE { ... }
```

Nothing moves until the save has committed: success is only ever reported after it. The items and gold reach each
client in that tick's ordinary `SMSG_INVENTORY_UPDATE`, after `SMSG_TRADE_CLOSED`. Received items land whole, in the
lowest free Bag slots, never merged into existing stacks. If the save is refused, both get `SMSG_TRADE_CLOSED
{ SaveFailed }` and nothing moved. While completing, a player who logs out or changes map does not stop it: the
outcome is still applied (a player who logged out is told nothing; the other gets the result).

During completion the receiver's arriving items have their Bag slots set aside, so loot, a quest reward or a purchase
made in that moment may be refused as a full Bag, the gold cap or a Unique item already owned, as if the trade had
already landed.

### 4.6 Closed

`SMSG_TRADE_CLOSED` ends the window; nothing moved unless `Result` is `Ok`. Its `Result` is, to each player:
- `Cancelled` to the one who cancelled, `CancelledByPartner` to the other;
- `OutOfRange`, `EnteredCombat`, `Died`, `Left`, `NpcWindowOpened` to both (checked every tick, and at once on a map
  change or when a shop or the bank opens);
- `Left` to the other player when one logs out or disconnects (the leaver is told nothing);
- `Ok` or `SaveFailed` at the end of completing (4.5); `Error` if the save could not start.

A client that loses its connection or leaves the world forgets the window; nothing about it survives a relog.

## 5. What can be traded

- Items from the **Bag** only: never Equipment or the Bank. A stack goes in whole; split it in the Bag first.
- Every item is tradeable unless its template is NoTrade or a quest item (`NotTradeable`).
- Up to 15 items a side, and any gold amount from 0 to the player's balance. A balance may not pass the gold cap after
  the trade (`GoldCapReached`), and no one may end up with two copies of a Unique item (`UniqueAlreadyOwned`).

## 6. Locked items elsewhere

While an item is offered it stays in the Bag but is **locked**. Every other request that would touch it is refused
with a new value, appended to each enum (a client built before it reads an unknown value):

| Enum | Value | Refused |
|---|---|---|
| `ItemRequestResult` (`SMSG_ITEM_RESULT`) | `Locked` 14 | A move, split, merge or swap of the item, or onto it (banking and dragging to Equipment included), or onto a Bag slot a completing trade set aside; destroying it. |
| `ItemUseResult` (`SMSG_ITEM_USE_RESULT`) | `Locked` 18 | Using or equipping it. An item cast whose item becomes locked before the bar ends is answered `Interrupted`. |
| `VendorResult` (`SMSG_VENDOR_RESULT`) | `Locked` 14 | Selling it. Offered gold cannot pay for a purchase (`NotEnoughGold`), and an offered stack does not pay an item cost (`MissingItems`). |

A quest turn-in does not take a locked Collect stack: with it locked the quest is `NotReady`, and an abandon leaves it
in the Bag. Show offered items as locked in the Bag, and the balance less the offered gold as what can be spent.
