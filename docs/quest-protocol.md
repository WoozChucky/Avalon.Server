# Quest Protocol

> **Audience:** client developers and LLMs handed the client codebase.
> **Status:** quests, V1 (#433). Amended in place when the quest protocol changes.

This is the client contract for quests. Quests are offered and handed in through NPC dialogue, progress on
the server, and are described to the client in full by the packets themselves: every text is already
resolved in the character's language, so a client needs no quest catalog of its own. For tooling (a quest
browser, documentation), `tools/Avalon.Exporter -- quest-catalog` writes `schema/quests/quest-catalog-v1.json`
(#714): every quest's id, enUS title, stages, objectives (id, type, target, count) and rewards. Game masters can
also read whole quests over REST, `GET /world/{worldId}/quest-template` and `/{id}`. The wire schema is `schema/avalon.proto` (re-exported with
`tools/Avalon.Exporter`); field numbers below are the `[ProtoMember]` numbers in it. Server-side rules are
described in `CLAUDE.md` under "Quests".

## 1. Opcodes

Client to server. Every one is accepted only while the character is in the world (in a map), and every one
is answered with exactly one `SMSG_QUEST_RESULT` (section 4).

| Opcode | Value | Packet | Fields |
|---|---|---|---|
| `CMSG_QUEST_ACCEPT` | `0x20C0` | `CQuestAcceptPacket` | 1 `QuestId` (uint32), 2 `NpcGuid` (uint64, raw `ObjectGuid` of the NPC the conversation is with) |
| `CMSG_QUEST_TURN_IN` | `0x20C1` | `CQuestTurnInPacket` | 1 `QuestId` (uint32), 2 `NpcGuid` (uint64, as above) |
| `CMSG_QUEST_ABANDON` | `0x20C2` | `CQuestAbandonPacket` | 1 `QuestId` (uint32) |

Server to client.

| Opcode | Value | Packet | Fields |
|---|---|---|---|
| `SMSG_QUEST_OFFER` | `0x30C0` | `SQuestOfferPacket` | 1 `QuestId`, 2 `NpcGuid`, 3 `Mode` (`QuestOfferMode`), 4 `Quest` (`QuestDisplayDto`) |
| `SMSG_QUEST_RESULT` | `0x30C1` | `SQuestResultPacket` | 1 `Result` (`QuestResult`), 2 `QuestId` (as sent in the request) |
| `SMSG_QUEST_LOG` | `0x30C2` | `SQuestLogPacket` | 1 `Quests` (repeated `QuestLogEntryDto`), 2 `CompletedQuestIds` (repeated uint32) |
| `SMSG_QUEST_UPDATE` | `0x30C3` | `SQuestUpdatePacket` | 1 `QuestId`, 2 `Kind` (`QuestUpdateKind`), 3 `State` (`QuestStateKind`), 4 `Stage`, 5 `Display` (`QuestDisplayDto`, only on `Accepted`), 6 `Progress` (repeated `QuestProgressDto`) |
| `SMSG_QUEST_MARKERS` | `0x30C4` | `SQuestMarkersPacket` | 1 `Markers` (repeated `QuestMarkerDto`) |

Every enum below is append-only, and 0 (`Unknown`) is what a payload without the field decodes as; a server
never sends it on purpose.

**Retired.** The old quest requests are gone: `CMSG_QUEST_STATUS` (`0x2040`), `CMSG_QUEST_LIST` (`0x2041`)
and `CMSG_QUEST_QUERY_AVAILABLE` (`0x2042`), with their packet classes. Their values were deleted from
`NetworkPacketType` and dropped out of `schema/opcodes.json` and `schema/avalon.proto`; nothing ever handled
them. A client must re-vendor the schema and delete any emitters for them.

## 2. Dialogue

Quests ride on the NPC conversation (`CMSG_INTERACT`, `SMSG_DIALOGUE_NODE`, `CMSG_DIALOGUE_CHOOSE`,
`SMSG_DIALOGUE_END`; the server rules are in `CLAUDE.md` under "Town NPCs are creatures").

**Quest options.** When the server sends an NPC's **root** node (the node a conversation opens on), it puts
the character's quest options for that NPC first, before the node's own authored options. Other nodes never
carry quest options. In order:
1. one turn-in option for each quest this NPC takes back that the character holds ready to turn in, by quest id;
2. one offer option for each quest this NPC gives that the character may accept now, by level requirement,
   then quest id. A quest that the character could accept but for a full log is still offered, so that the
   accept is answered `LogFull` rather than the quest silently hiding.

Each quest option is an `SDialogueOptionInfo` with:
- `OptionId` (field 1) = the quest id **negated** (quest 3 is option -3). Authored options are never negative,
  so the two never collide.
- `Text` (field 2) = the quest's title.
- `Kind` (field 3) = `QuestOffer` (3) or `QuestTurnIn` (4) (`DialogueOptionKind`).
- `QuestId` (field 4) = the quest id. Set on quest options only.

A client that does not know a `Kind` value must not offer that option (the rule for every `DialogueOptionKind`).

**Choosing a quest option** (`CMSG_DIALOGUE_CHOOSE` with the negative `OptionId`, the root's `NodeId` and the
NPC's guid) sends `SMSG_QUEST_OFFER` with the whole quest, `Mode` `Offer` (1) for an offer or `TurnIn` (2) for a
turn-in. The conversation stays on the root: no new dialogue node is sent, and nothing else changes. An option
the server does not offer this character at this node now is ignored (logged, no reply). The choose passes the
usual dialogue checks first, as for any other option: a dead character's choose is dropped, and an NPC that is
gone, dead or past the 6 m dialogue leash ends the conversation with `SMSG_DIALOGUE_END`.

**Declining** an offer needs no packet: the client closes the offer window. **Accepting** is
`CMSG_QUEST_ACCEPT`; **handing in** is `CMSG_QUEST_TURN_IN`, each naming the quest and the NPC. The server does
not require the offer to have been shown; it checks the conversation and the quest afresh.

**After an accepted accept or turn-in** (result `Ok`) the server sends the NPC's root node again
(`SMSG_DIALOGUE_NODE`), so its quest options are current (a turned-in quest's option is gone; a follow-up
quest may now be offered). This goes out before the `SMSG_QUEST_RESULT`. When the NPC is also a vendor or a
banker and its shop or bank is open, the root is re-sent all the same and the shop or bank window **stays open**:
the conversation is still with that NPC.

**Talking counts.** Opening a conversation with an NPC that a held quest's current stage asks you to speak
to completes that objective before the node is built, so a quest that becomes ready by that conversation
already shows its turn-in option on it.

## 3. Display data

`QuestDisplayDto` is everything a client shows about a quest, with every text resolved in the character's
language:

| Field | Type | What |
|---|---|---|
| 1 `Title` | string | The quest's title. |
| 2 `Text` | string | The description; in a `TurnIn` offer, the completion text (what the NPC says when it is handed in). |
| 3 `Stages` | repeated `QuestStageDto` | Every stage, in order. |
| 4 `Rewards` | `QuestRewardsDto` | What a turn-in pays. |

`QuestStageDto`: 1 `Sequence` (int, from 0), 2 `Text` (the stage's line, empty when it has none),
3 `Objectives` (repeated `QuestObjectiveDto`).

`QuestObjectiveDto`: 1 `ObjectiveId` (uint32, unique across all quests), 2 `Kind` (`QuestObjectiveKind`:
`Kill` 1, `Collect` 2, `Talk` 3, `Scripted` 4), 3 `TargetId` (uint64: the creature template for `Kill` and
`Talk`, the item template for `Collect`, 0 for `Scripted`), 4 `Count` (how many are needed), 5 `Text` (e.g.
"Thornback Boars slain").

`QuestRewardsDto`: 1 `Experience`, 2 `Money` (copper), 3 `Items` (repeated `QuestRewardItemDto`:
1 `ItemTemplateId`, 2 `Count`). Rewards are fixed; there is no choice of reward.

A quest's stages run in order: all of a stage's objectives (at most 4) complete it and the next stage starts;
after the last, the quest is ready to turn in.

## 4. Answers

Every request is answered with exactly one `SMSG_QUEST_RESULT` carrying the request's `QuestId`. It is sent
after any dialogue packet the request caused (the re-sent root), and **before** the `SMSG_QUEST_UPDATE` and
system lines it caused, which go out at the end of the tick (section 6). A connection with no character in
the world gets no answer. A request that threw on the server is logged and answered `Error`. An accept or a
turn-in that has happened is answered `Ok` even if re-sending the NPC's root then failed (logged); the client
then keeps the dialogue node it had until it talks to the NPC again.

| Value | Name | Meaning |
|---|---|---|
| 0 | `Unknown` | Never sent; a payload without the field. |
| 1 | `Ok` | Done. |
| 2 | `NotAvailable` | The quest does not exist, this NPC does not give it, or this character may not accept it now (already held, already completed, prerequisite not completed, level or class not met, or refused by the quest's script). |
| 3 | `NotActive` | The character does not hold that quest (or the server no longer knows it). |
| 4 | `NotReady` | The quest is not ready to turn in, or its items are no longer all in the bag. |
| 5 | `LogFull` | The character already holds the most quests allowed (20 by default). |
| 6 | `BagFull` | The reward items would not fit in the bag, even after the quest items leave it. |
| 7 | `MoneyCap` | The reward money would take the character past its gold cap. |
| 8 | `TooFar` | The NPC is past the 6 m dialogue leash; the conversation was ended (`SMSG_DIALOGUE_END`). |
| 9 | `NoConversation` | No open conversation with that NPC, the NPC is gone or dead (the conversation was ended), or the character is dead; on a turn-in, also an NPC that does not take that quest back. |
| 10 | `Error` | The server failed while handling the request, or a reward cannot be paid (a data fault, logged). Changes a turn-in had already applied are not rolled back. |

The checks run in this order, and the first failure is the answer:
- **Accept:** `NoConversation` (dead, no open conversation, or a different NPC; then the NPC gone or dead),
  `TooFar`, `NotAvailable` (unknown quest, or this NPC is not its giver), then availability: `NotAvailable`
  (completed or held, prerequisite, level, class, script), and last `LogFull`.
- **Turn in:** `NoConversation`, `TooFar` (as for accept), `NotActive` (not held, or unknown), `NoConversation`
  (this NPC is not the quest's ender), `NotReady` (not ready, or the last stage's items are not all in the
  bag as it is now), `Error` (a reward item the server can no longer pay), `BagFull`, `MoneyCap`. A refused
  turn-in takes nothing and pays nothing. (The bag is recounted first, so a quest found short goes back to
  `Active` and its `Progress` update follows.)
- **Abandon:** `NotActive`. Abandon works anywhere, with no conversation.

## 5. The log

`SMSG_QUEST_LOG` is the character's whole quest log, sent **once**, on the tick the selected character enters
the world (after a Change Character too). A client replaces what it held.

`QuestLogEntryDto`, one per held quest, by quest id:

| Field | What |
|---|---|
| 1 `QuestId` | |
| 2 `State` | `QuestStateKind`: `Active` 1, `ReadyToTurnIn` 2. |
| 3 `Stage` | The current stage's sequence. |
| 4 `Display` | The `QuestDisplayDto`. |
| 5 `Progress` | Every objective of the quest, in every stage, with its count (`QuestProgressDto`: 1 `ObjectiveId`, 2 `Progress`); 0 for one not reached yet. |

`CompletedQuestIds` lists every quest the character has turned in. A quest the character holds that the
server no longer knows (removed from the data) is left out of the log; it can still be abandoned by id.

The log already shows the counts as they are at login (the bag is recounted at select), so a login is
followed by no progress lines for them.

## 6. Updates

After the log, each change to a quest arrives as one `SMSG_QUEST_UPDATE`, **at most one per quest per tick**,
sent at the end of the tick in quest id order:

| `Kind` | When | What it carries |
|---|---|---|
| `Accepted` (1) | The quest was accepted this tick. | `State`, `Stage`, `Progress` and `Display` (as a log entry). Add it to the log, or replace the entry when the client still holds the quest (see below). |
| `Progress` (2) | A count, the stage or the state changed. | `State`, `Stage`, `Progress` (every objective, as in the log); no `Display`. Replace the entry's state, stage and counts. |
| `Removed` (3) | The quest was abandoned. | Only `QuestId`. Drop it from the log. |
| `Completed` (4) | The quest was turned in. | Only `QuestId`. Drop it from the log and add the id to the completed set. |

An accept stays `Accepted` for the rest of its tick, whatever progress follows in the same tick, so the update
carries the display. A quest accepted and abandoned in the same tick arrives as `Removed` only; a client
ignores a `Removed` for a quest it never had. A quest accepted and turned in in the same tick arrives as
`Completed` only; a client just adds the id to the completed set. A quest abandoned and accepted again in the
same tick arrives as `Accepted` only, for a quest the client still holds: a client replaces that entry with the
one the update carries (the new accept's stage and counts).

**After a reload of the quest data.** When the server's quest data is reloaded with a lower count or an objective
removed, every held quest is settled once at the end of the next tick: a count above its new target comes down to it,
and a stage now complete starts the next or makes the quest ready to turn in. Each such quest arrives as an ordinary
`Progress` update, with the usual system lines. A reload that lands while the player is offline is already reflected
in the log at login.

**Order within a tick.** For one connection, the end of the tick sends, in this order: the quest updates, the
quest system lines (section 9), the markers (section 7), and only then `SMSG_INVENTORY_UPDATE`. So a turn-in's
`Completed` arrives **before** the inventory update carrying the reward items and gold (and the quest items
leaving), and a `Progress` for a collected item arrives before the inventory update that shows it in the bag.

## 7. Markers

`SMSG_QUEST_MARKERS` lists every quest NPC (a creature that gives or takes back any quest) in the character's
current instance, with what it shows over its head for this character. It is the whole list each time; a
client replaces what it held.

`QuestMarkerDto`: 1 `CreatureGuid` (uint64, raw `ObjectGuid`), 2 `Marker` (`QuestMarker`):

| Value | Name | Meaning |
|---|---|---|
| 1 | `None` | Nothing to show. |
| 2 | `Available` | It offers a quest the character may accept (a full log still shows `Available`). |
| 3 | `ReadyToTurnIn` | It takes back a quest the character holds ready to turn in. Outranks `Available`. |

It is worked out again on entering the world, on entering another instance, and when an accept, progress, a
turn-in, an abandon, a level-up or a reload of the quest data may have changed it, and it is **sent only when
the list differs from the last one sent**, across instances too. So an arrival whose list equals the last one
sends nothing: two instances in a row with no quest NPC (both empty lists), or a transfer back into the same
town instance (where every creature is removed and added again through `SMSG_WORLD_STATE_REMOVE` and fresh
adds, but the markers stay the same). A client therefore keeps the marker list, keyed by creature guid, until
the next `SMSG_QUEST_MARKERS` replaces it, and never clears it on a map transition or a world-state remove. The
list may be empty (an instance with no quest NPC). It covers **every** quest NPC in the instance, near or far:
there is no interest radius, so a marker may name a creature the client has not been sent yet; keep it and
apply it when the creature appears. The list is worked out from the creatures present when one of those
inputs changes; a quest NPC spawned or removed later is not reflected until the next change (the seeded quest
NPCs are town NPCs, which stand for good).

## 8. Quest items

- A `Collect` objective counts the item **in the bag only**. Moving copies to the bank, destroying them, or
  any other way they leave the bag lowers the count, and a quest that was ready goes back to `Active`
  ("X: no longer ready to turn in."). Copies already in the bag when the quest (or the stage) starts count at
  once.
- Quest items are flagged `QuestItem` (2048 in `ItemTemplateFlags`). They are never sold to or by a vendor.
- A quest item drops only for a character that still needs it: each eligible character who shares the kill
  (see `docs/party-protocol.md`, Loot) and still has fewer in the bag than the current stage asks for gets its
  own roll, and each success is one item **reserved for that character for good**: `LootDropDto.OwnerCharacterId`
  is that character and `FreeForAllAt` is `DateTime.MaxValue.Ticks`, so nobody else can ever take it. Quest
  drops are placed in the same ring as the kill's other drops.
- A pickup of a quest item is refused `NotYours` (`SLootPickupResultPacket`) when the picker no longer needs it
  (enough in the bag already, the quest abandoned, handed in or past that stage), even by the character it was
  reserved for. The drop stays on the ground. Drops still on the ground do not count, so a character short of
  its count can see more reserved drops than it will be allowed to pick up.
- A turn-in and an abandon take **every** copy of the quest's items from the bag and the bank.

## 9. System lines

Quest milestones are `SChatMessagePacket` lines on `ChatChannel.System` (sender "System"), sent at the end of
the tick after the updates (X is the quest's title, in the character's language; the lines themselves are in
English):
- "Quest accepted: X."
- "<objective text>: n/m" (a count changed, e.g. "Thornback Boars slain: 3/6")
- "X: stage complete."
- "X: ready to turn in."
- "X: no longer ready to turn in."
- "Quest completed: X."
- "Quest abandoned: X." (a quest the server no longer knows is named "#id")

## 10. Seeded quests

The storyline "Trouble in the Forest", in the town (map 1) and the forest:

| Quest | Giver → ender | Needs | Asks | Pays |
|---|---|---|---|---|
| 1 Thinning the Herd | Uriel → Uriel | level 1 | kill 6 Thornback Boars | 150 experience, 100 copper |
| 2 Tusks for Borin | Uriel → Borin Stoutbeard | quest 1 | collect 4 Boar Tusks (item 57; 60 % from Thornback Boars) | 250 experience, 150 copper, 2 Greater Health Potions (item 56) |
| 3 The Alpha's Howl | Borin → Borin | level 2, quest 2 | stage 0: kill 3 Grey Fen Wolves and 2 Husks of the Wold; stage 1: speak with Marta Ledgerwell; stage 2: kill the Bramblemaw Alpha | 600 experience, 400 copper, the Alpha's Fang Pendant (item 58, a neck piece) |
