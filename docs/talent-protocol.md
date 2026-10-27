# Talent Protocol

> **Audience:** client developers and LLMs handed the client codebase.
> **Status:** the talent tree, Part B (#1181). Amended in place when the talent protocol changes.

This is the client contract for the talent tree. The wire schema is `schema/avalon.proto` (re-exported with
`tools/Avalon.Exporter`); field numbers below are the `[ProtoMember]` numbers in it. Server-side rules are described in
[Talents](talents.md).

## 1. What the client draws from

The tree itself never travels in a packet: it is `schema/talents/talent-catalog-v1.json`, generated from the seed
(`dotnet run --project tools/Avalon.Exporter -- talent-catalog`). It lists:

- `nodes`, in id order: `id`, `kind` (1 start, 2 small, 3 notable, 4 ability, 5 capstone, 6 keystone, 7 junction),
  `name` (null on a small node and a junction), `angle` (degrees, in (-180, 180]) and `radius` (layout units) for
  drawing the wheel, `forkGroupId`, `abilityId` (the ability a start or an ability node grants, an id of
  `schema/abilities/ability-catalog-v1.json`) and `stats` (`stat`, a `StatType` number of `item-schema-v1.json`;
  `kind`, 1 added or 2 more; `value`; `valueMax`, the high end of an "adds X to Y" line);
- `edges`, undirected, each once with the smaller id first;
- `forkGroups`, empty until capstones are seeded;
- `classStarts`: the node each class starts on (`class` 1 Warrior, 2 Wizard, 3 Hunter, 4 Cleric).

Every enumeration is a number, listed in the file's `$comment`. A node id means nothing and never changes: it is what
a character's saved talents and every packet below name. The same tree is served over REST at
`GET /public/world/{worldId}/talents`, as the world's database holds it ([multi-world API](api-worlds.md#public-routes)).

A character holds its class's start for free and never allocates it. It holds the start's ability (its starter: Heavy
Strike, Spark Bolt, Quick Shot or Smite) and the ability of every ability node it holds; nothing else grants an
ability.

## 2. Opcodes

Client to server. Accepted only while the character is in the world (in a map, `MapSessionFilter`); before that the
request is dropped unanswered.

| Opcode | Value | Packet | Fields |
|---|---|---|---|
| `CMSG_TALENT_ALLOCATE` | `0x20E0` | `CTalentAllocatePacket` | 1 `RequestId` (uint32), 2 `NodeIds` (repeated uint32) |

Server to client, to the character's own connection only.

| Opcode | Value | Packet | Fields |
|---|---|---|---|
| `SMSG_TALENT_RESULT` | `0x30F0` | `STalentResultPacket` | 1 `RequestId` (uint32, echoed), 2 `Result` (`TalentResult`), 3 `NodeId` (uint32) |
| `SMSG_TALENT_STATE` | `0x30F1` | `STalentStatePacket` | 1 `NodeIds` (repeated uint32), 2 `PointsEarned` (uint32), 3 `PointsSpent` (uint32) |

All three are encrypted.

## 3. Allocating

`CMSG_TALENT_ALLOCATE` adds nodes **all or nothing**: every node named is allocated, or none is. A client plans
several nodes in its window and commits them with one request. `NodeIds` are taken in the order sent, so a node may
connect through one named before it: list them in the order a path walks from what the character already holds.
`RequestId` is the client's own, echoed in the result so it can pair answers with requests.

Every request with a character in the world gets **exactly one** `SMSG_TALENT_RESULT`. A refusal changes nothing: no
node, no point, no stat, no ability.

An empty `NodeIds` is `Ok` and changes nothing (no state follows).

## 4. `TalentResult`

A `byte` enum, append-only. The server checks the rules in this order and answers the first one broken:

1. `Dead` (1): the character is dead.
2. `TooMany` (2): the request names more nodes than the character has points left to spend (earned minus spent; so any
   node at all when none are left), or more than the tree holds. **While the world's tree is unavailable** (its
   catalog refused the tree at startup or at a reload: a bad deploy), the tree holds no node, so every non-empty
   request is answered `TooMany` whatever the character's points. A client that shows `TooMany` as "not enough points"
   while the state shows points left to spend should say instead that talents are unavailable.
3. Then each id, in the order sent; the first id that breaks a rule is named in `NodeId`:
   - `UnknownNode` (3): 0, above 2,147,483,647, or not a node of the tree;
   - `AlreadyAllocated` (4): already held, named earlier in the same request, or a class start (never allocated);
   - `NotConnected` (5): none of its neighbours is the character's class start, a node it holds, or a node named
     earlier in the request;
   - `ForkLocked` (6): its fork partner is held or named earlier. No fork group is seeded yet, so this is not sent
     until capstones are.

The other values:

| Value | Name | Meaning |
|---:|---|---|
| 0 | `Ok` | Every node named is held now. Changed what the window shows, so `SMSG_TALENT_STATE` follows (section 5). |
| 7 | `InternalError` | Something on the server failed; it is logged. A failure before the nodes were held changed nothing; one after leaves them held, and the state follows as for `Ok`. |

`NodeId` is the refused node for `UnknownNode`, `AlreadyAllocated`, `NotConnected` and `ForkLocked` (the id as sent,
so 0 when the refused id was 0), and 0 for `Ok`, `Dead`, `TooMany` and `InternalError`.

## 5. The state, and when it arrives

`SMSG_TALENT_STATE` is what the talent window shows, **whole every time**: `NodeIds`, every node the character holds,
ascending (never its class start); `PointsEarned`, one per level plus the talent points of every quest it has turned in
(section 6); `PointsSpent`, one per node held. What it may still spend is earned minus spent. The client replaces what
it holds with each state. Spent is above earned only while the world's tree is unavailable (section 4, `TooMany`): a
character's saved nodes are then kept as they are, and nothing can be allocated.

It arrives:

- **At select**, after `SInventorySnapshotPacket` and `SCharacterAbilitiesPacket`, before the ignore list
  ([login flow](character-login-flow.md)). Select first drops, free, the saved nodes that no longer fit the live tree
  (a node a later tree removed, then a node no longer linked to the class start through held nodes, then every node
  when they cost more than the points earned), so the first state is already the corrected one. A character that
  lost nodes this way gets its points back and no other message.
- **After an accepted allocation**: the request's `SMSG_TALENT_RESULT` (`Ok`) first, then the state at the end of the
  same tick.
- **After a level-up**, whatever gave it (a kill, a quest, an item, a command), at the end of that tick: one more point.
- **After a turn-in of a quest that pays talent points**: the system line "You gained 1 talent point." (or "You gained
  N talent points.") right after "Quest completed: X.", and the state at the end of the same tick
  ([quest protocol](quest-protocol.md), section 9). A repeatable quest pays none.

A tick that changes the state several times (an allocation and a level-up together) sends it once.

**The abilities after an allocation.** When an allocation changes the abilities the character holds (it allocated an
ability node), `SMSG_CHARACTER_ABILITIES` (`0x3027`, `SCharacterAbilitiesPacket`) arrives again, whole, before the
request's result: the starter first, then each held ability node's ability by ascending node id. An ability already
held keeps its running cooldown; a new one arrives ready. An allocation of small or notable nodes sends no ability
list: the stats it changes reach the client as usual, through `SMSG_CHARACTER_STATS` (`0x302A`) and, when an ability's
amount or pace moved, `SMSG_CHARACTER_ABILITY_AMOUNTS` (`0x302C`), at the end of the tick
([inventory and saves](inventory-and-saves.md)). The ability list has no fixed length: an action bar takes any number
of abilities.

## 6. Quest talent points

`QuestRewardsDto` carries field 5 `TalentPoints` (uint32): the talent points a quest's turn-in pays, 0 for most quests
and for every repeatable one. It travels wherever a quest's rewards do (the offer, the log and the accept,
[quest protocol](quest-protocol.md)). Three forest quests pay 1 each today: 3 The Alpha's Howl, 7 Heartwood and 8
Mother of Thorns (the quest protocol's seeded tables). The quest catalog export carries it as `talentPoints` in
`rewards` (`schema/quests/`).

## 7. REST

The REST API reads the same figures from the saved rows ([multi-world API](api-worlds.md)):
`GET /world/{worldId}/character/{id}/talents` returns `CharacterTalentsDto` (`characterId`, `nodeIds` ascending,
`pointsEarned`, `pointsSpent`), as the character's world last saved them; and `GET /world/{worldId}/character/{id}/abilities`
derives the character's abilities from the tree: the class start's starter, then each held ability node's.
