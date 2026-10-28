# Talent Protocol

> **Audience:** client developers and LLMs handed the client codebase.
> **Status:** the talent tree, Parts B, C and D (#1181). Amended in place when the talent protocol changes.

This is the client contract for the talent tree. The wire schema is `schema/avalon.proto` (re-exported with
`tools/Avalon.Exporter`); field numbers below are the `[ProtoMember]` numbers in it. Server-side rules are described in
[Talents](talents.md).

## 1. What the client draws from

The tree itself never travels in a packet: it is `schema/talents/talent-catalog-v1.json`, generated from the seed
(`dotnet run --project tools/Avalon.Exporter -- talent-catalog`). It lists:

- `nodes`, in id order: `id`, `kind` (1 start, 2 small, 3 notable, 4 ability, 5 capstone, 6 keystone, 7 junction),
  `name` (null on a small node and a junction), `angle` (degrees, in (-180, 180]) and `radius` (layout units) for
  drawing the wheel, `forkGroupId` (the capstone fork, of which a character holds one node at most; null on every
  other node), `abilityId` (the ability a start or an ability node grants, an id of
  `schema/abilities/ability-catalog-v1.json`), `keystone` (the rule a keystone bends, section 8; null on every other
  node and on a keystone of stat lines only), `stats` (`stat`, a `StatType` number of `item-schema-v1.json`; `kind`,
  1 added or 2 more; `value`; `valueMax`, the high end of an "adds X to Y" line) and `modifiers` (how a capstone
  reshapes its ability, by kind: `kind`, `abilityId`, `value`, `value2`; empty on every node but a capstone,
  section 8);
- `edges`, undirected, each once with the smaller id first;
- `forkGroups`: the nine capstone forks, by id;
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
| `CMSG_TALENT_REFUND` | `0x20E1` | `CTalentRefundPacket` | 1 `RequestId` (uint32), 2 `LecternGuid` (uint64), 3 `NodeIds` (repeated uint32) |

Server to client, to the character's own connection only.

| Opcode | Value | Packet | Fields |
|---|---|---|---|
| `SMSG_TALENT_RESULT` | `0x30F0` | `STalentResultPacket` | 1 `RequestId` (uint32, echoed), 2 `Result` (`TalentResult`), 3 `NodeId` (uint32) |
| `SMSG_TALENT_STATE` | `0x30F1` | `STalentStatePacket` | 1 `NodeIds` (repeated uint32), 2 `PointsEarned` (uint32), 3 `PointsSpent` (uint32), 4 `RefundPricePerNode` (uint64) |

All four are encrypted.

## 3. Allocating

`CMSG_TALENT_ALLOCATE` adds nodes **all or nothing**: every node named is allocated, or none is. A client plans
several nodes in its window and commits them with one request. `NodeIds` are taken in the order sent, so a node may
connect through one named before it: list them in the order a path walks from what the character already holds.
`RequestId` is the client's own, echoed in the result so it can pair answers with requests.

Every request with a character in the world gets **exactly one** `SMSG_TALENT_RESULT`. A refusal changes nothing: no
node, no point, no stat, no ability.

An empty `NodeIds` is `Ok` and changes nothing (no state follows).

### Refunding

`CMSG_TALENT_REFUND` gives nodes back, **all or nothing**, and only at the Lectern of Paths: the doodad in
Glimmerdell's market whose entry in the doodad catalog (`schema/doodads/doodad-catalog-v1.json`, template 8) has
`service` 2. A click on it opens the respec window, as a click on the Workbench (`service` 1) opens its window; the
click itself sends nothing new. The client plans refunds in that window and commits them with one request naming the
Lectern's guid (as its doodad spawn carried it) and the nodes. **"Reset all" is every node held.** `NodeIds` may come
in any order: the server judges the refund as a whole, and accepts it when every node still held afterwards reaches
the class's start through held nodes. Refunding the held side of a fork frees the other side.

The Lectern and its range (5 m) are checked on every request, so a player who walks away with the window open is
refused `TooFar`, and a guid naming any other doodad, the Workbench included, is refused `NotFound`.

**The price.** Free up to and including level 10. From level 11 each node costs gold, by the character's level when
it refunds: 100 a node at level 11, then 25 more for each level (125 at 12, 200 at 15, 575 at 30, 1,075 at 50, 2,325
at 100; the server's `Game:Talents` settings). The state carries the price of one node, `RefundPricePerNode`
(section 5), so the window shows a refund's cost as `RefundPricePerNode` times the nodes named, free when it is 0.
Gold offered in an open trade does not count toward paying it.

Every refund request with a character in the world gets **exactly one** `SMSG_TALENT_RESULT`, the same packet an
allocation gets. A refusal changes nothing: no node, no point, no gold, no stat, no ability. An empty `NodeIds` at the
Lectern is `Ok` and changes nothing (no state follows); away from it, it is refused as any refund is.

## 4. `TalentResult`

A `byte` enum, append-only. Values 8-14 were appended for refunds after `InternalError` (7), so the values do not
follow the order the rules are asked in. For an allocation, the server checks the rules in this order and answers the
first one broken:

1. `Dead` (1): the character is dead.
2. `TooMany` (2): the request names more nodes than the character has points left to spend (earned minus spent; so any
   node at all when none are left, or when spent is above earned, section 5), or more than the tree holds. **While the
   world's tree is unavailable** (its catalog refused the tree at startup or at a reload: a bad deploy), the tree holds
   no node, so every non-empty request is answered `TooMany` whatever the character's points. A client that shows
   `TooMany` as "not enough points" while the state shows points left to spend (earned above spent) should say instead
   that talents are unavailable; with none left, or spent above earned, it is "not enough points".
3. Then each id, in the order sent; the first id that breaks a rule is named in `NodeId`:
   - `UnknownNode` (3): 0, above 2,147,483,647, or not a node of the tree;
   - `AlreadyAllocated` (4): already held, named earlier in the same request, or a class start (never allocated);
   - `NotConnected` (5): none of its neighbours is the character's class start, a node it holds, or a node named
     earlier in the request;
   - `ForkLocked` (6): its fork partner is held or named earlier: the other capstone of its fork (section 8).

For a refund, in this order:

1. `Dead` (1): the character is dead.
2. `Stunned` (8): the character cannot act.
3. `NotFound` (9): no doodad of that guid in the character's instance, or it is not a Lectern.
4. `TooFar` (10): the character is beyond the Lectern's range.
5. `Casting` (11): the character is casting an ability (using an item or a doodad does not count), so no ability
   leaves mid-cast.
6. Then each id, in the order sent; the first id that breaks a rule is named in `NodeId`:
   - `UnknownNode` (3): 0, above 2,147,483,647, or not a node of the tree. While the world's tree is unavailable, every
     id is unknown, so every non-empty refund is refused `UnknownNode`;
   - `NotAllocated` (12): not held, named earlier in the same request, or a class start (never held).
7. `WouldDisconnect` (13): the refund would leave a held node cut off from the class's start. `NodeId` names the node
   it would strand, the lowest id if there are several, so the window can show which node holds the refund back.
8. `NotEnoughGold` (14): the price (section 3, "Refunding") is above 0 and more than the character can spend, gold
   offered in an open trade left out.

The other values:

| Value | Name | Meaning |
|---:|---|---|
| 0 | `Ok` | Every node named is held now (an allocation), or no longer held and paid for (a refund). Changed what the window shows, so `SMSG_TALENT_STATE` follows (section 5). |
| 7 | `InternalError` | Something on the server failed; it is logged. A failure before the nodes changed changed nothing (a refund's gold is given back); one after leaves the change made (a refund stays paid for), and the state follows as for `Ok`. |

`NodeId` is the refused node for `UnknownNode`, `AlreadyAllocated`, `NotConnected`, `ForkLocked` and `NotAllocated`
(the id as sent, so 0 when the refused id was 0), the node the refund would strand for `WouldDisconnect`, and 0 for
every other value.

## 5. The state, and when it arrives

`SMSG_TALENT_STATE` is what the talent window shows, **whole every time**: `NodeIds`, every node the character holds,
ascending (never its class start); `PointsEarned`, one per level plus the talent points of every quest it has turned in
(section 6); `PointsSpent`, one per node held; `RefundPricePerNode`, the gold one node costs to refund at the
character's level, 0 while refunds are free (section 3, "Refunding"). What it may still spend is earned minus spent.
The client replaces what it holds with each state.

**Spent above earned.** Spent is above earned when the points earned fell while the nodes were kept: a reload or a
deploy refused or removed a quest the character turned in that paid talent points, or lowered what it pays, or staff
lowered the character's level (a REST character patch, which the world reads at the next select). Nothing is dropped
while the character is in the world, and a reload re-sends no state. At its next select, with the tree loaded, **the
whole tree is cleared**, whatever lowered the points: every node is dropped, free, and the character spends its points
again (below, at select). A refused tree drops nothing at select, so spent stays above earned as long as the tree is
refused. Nothing can be allocated while spent is at or above earned (`TooMany`).

**While the tree is unavailable**, the state still lists the nodes held, but they grant nothing: no stat, and no
ability beyond the class start's starter, until a tree that holds them is loaded again.

It arrives:

- **At select**, after `SInventorySnapshotPacket` and `SCharacterAbilitiesPacket`, before the ignore list
  ([login flow](character-login-flow.md)). Select first drops, free, the saved nodes that no longer fit the live tree
  (a node a later tree removed, then a node no longer linked to the class start through held nodes, then every node
  when they cost more than the points earned, whatever lowered the points: a refused or removed paying quest clears
  the whole tree as a lowered level does), so the first state is already the corrected one. A character that lost
  nodes this way gets its points back and no other message. Only a refused tree keeps every saved node (it drops
  nothing); refused quests do not.
- **After an accepted allocation or refund**: the request's `SMSG_TALENT_RESULT` (`Ok`) first, then at the end of the
  same tick the ability list when the abilities held, or a capstone's shape of one, changed (below), then the state.
- **After a level-up**, whatever gave it (anything that grants experience: a kill, a quest, an item), at the end of
  that tick: one more point, and the refund price at the new level (from level 11 it changes with every level; the
  level-up from 10 to 11 shows 100 at once).
- **After a turn-in of a quest that pays talent points**: the system line "You gained 1 talent point." (or "You gained
  N talent points.") right after "Quest completed: X.", and the state at the end of the same tick
  ([quest protocol](quest-protocol.md), section 9). A repeatable quest pays none.

A tick that changes the state several times (an allocation and a level-up together) sends it once.

**The abilities after a change.** When an allocation or a refund changes the abilities the character holds (it
allocated or refunded an ability node), `SMSG_CHARACTER_ABILITIES` (`0x3027`, `SCharacterAbilitiesPacket`) arrives
again, whole, **at the end of the tick, after the request's result** and just before the state: the starter first,
then each held ability node's ability by ascending node id. It also arrives when a change keeps the same abilities but
changes how a capstone shapes one of them (a capstone allocated or refunded, a fork switched), carrying the new shape
(section 8). Since Part C it never arrives before the result, for allocations and refunds alike. A tick of several
such requests sends one list, as things stand after the last of them.
An ability already held keeps its running cooldown; a new one arrives ready, and so does one refunded and taken again,
even one refunded on cooldown. An ability a capstone reshapes keeps its running cooldown too. A change of small,
notable, junction or keystone nodes sends no ability list: the stats it changes reach the client as usual, through
`SMSG_CHARACTER_STATS` (`0x302A`) and, when an ability's amount or pace moved, `SMSG_CHARACTER_ABILITY_AMOUNTS`
(`0x302C`), at the end of the tick ([inventory and saves](inventory-and-saves.md)). The ability list has no fixed
length: an action bar takes any number of abilities.

## 6. Quest talent points

`QuestRewardsDto` carries field 5 `TalentPoints` (uint32): the talent points a quest's turn-in pays, 0 for most quests
and for every repeatable one. It travels wherever a quest's rewards do (the offer, the log and the accept,
[quest protocol](quest-protocol.md)). Three forest quests pay 1 each today: 3 The Alpha's Howl, 7 Heartwood and 8
Mother of Thorns (the quest protocol's seeded tables). The quest catalog export carries it as `talentPoints` in
`rewards` (`schema/quests/`).

## 7. REST

The REST API reads the same figures from the saved rows ([multi-world API](api-worlds.md)):
`GET /world/{worldId}/character/{id}/talents` returns `CharacterTalentsDto` (`characterId`, `nodeIds` ascending,
`pointsEarned`, `pointsSpent`), as the character's world last saved them, less any node the tree no longer holds; and
`GET /world/{worldId}/character/{id}/abilities` derives the character's abilities from the tree: the class start's
starter, then each held ability node's. Both read the rows as last saved: the world applies its login check at the
character's next select (a node cut off from the start, or more held than earned, is dropped then), and the REST reads
do not re-run it, nor the world catalogs' refusals.

## 8. Capstones and keystones

Part D seeds 26 capstones (nodes 306-331) and 4 keystones (332-335), in the catalog export (section 1) and the public
tree alike. Both are allocated and refunded as any other node, and every one acts in play.

**Capstones.** A capstone hangs from the last node of a branch, and its `modifiers` reshape the ability that branch
hangs from (`abilityId`, the same on each of a capstone's rows). Nine branches end in a fork of two capstones, the two
nodes sharing a `forkGroupId`: a character holds one, the other answering `ForkLocked`, until the held one is
refunded. A character's shape of an ability is the sum, per kind, of the held capstones' rows. The kinds:

| `kind` | Name | What `value` (and `value2`) mean |
|---:|---|---|
| 1 | Added projectiles | `value` more projectiles, in a fan 10° apart centred on the aim; a unit is hit at most once per cast |
| 2 | Impact explosion | where the projectile ends on a unit, a circle of `value` / 10 metres hits every other hostile unit in it at `value2` % of the amount, damage only |
| 3 | Area | a circle's radius, and an explosion's, × (1 + `value` / 100) |
| 4 | Cone reach | a cone's reach × (1 + `value` / 100) |
| 5 | Cone arc | a cone's arc × (1 + `value` / 100), at most 360° |
| 6 | Aura stacks | the ability's aura may stack `value` more times from this caster |
| 7 | Aura duration | the ability's aura lasts × (1 + `value` / 100) |
| 8 | Second hit | each unit affected is hit, or healed, again at once at `value` % of the amount |

`value2` is set on an impact explosion only. No capstone lengthens or deepens crowd control: the server refuses a tree
with a stack or duration modifier on an aura that stuns, roots, silences or slows.

**The shaped ability on the wire.** `AbilityInfo` in `SCharacterAbilitiesPacket` carries the character's shape of
each ability: fields 11 `Reach`, 12 `Radius` and 13 `ArcDegrees` hold the shaped values (what a client draws a cone,
a circle or the telegraph from), and six more fields the rest. An ability no held capstone shapes sends 1 projectile
and 0 for each of the others.

| Field | Name | Type | Meaning |
|---:|---|---|---|
| 25 | `ProjectileCount` | uint32 | Projectiles one cast fires, in a fan 10° apart centred on the aim, at most 9; 1 for any shape but a projectile |
| 26 | `ExplosionRadius` | float | Metres: the explosion where the projectile ends on a unit, already widened by any area modifier; 0 for none |
| 27 | `ExplosionEffectiveness` | float | Percent of the amount the explosion hits for |
| 28 | `SecondHitEffectiveness` | float | Percent of the amount each affected unit is hit, or healed, again at once; 0 for none |
| 29 | `AuraStacksBonus` | uint32 | Stacks the caster's aura may reach above its own cap |
| 30 | `AuraDurationPct` | float | Percent longer the caster's aura lasts |

A cast keeps the shape it was started with: one queued before a capstone is allocated fires as its telegraph showed,
and the next cast takes the new shape.

**What the client sees of each modifier.** No packet is new; each effect arrives in packets the client already
handles:

- **A fan** (`ProjectileCount` above 1). Each lane is a projectile of its own, a `SpellProjectile` world object added,
  moved and removed as any, each lane 10° from its neighbour, centred on the aim. Each lane sends its own
  `SMSG_ABILITY_FIRED` with its own projectile footprint, all under the cast's one `CastId`, so a fan of three is three
  fired events. A cast-time fan's `SUnitStartCastPacket` footprint is the centre lane: draw the telegraph as a fan from
  `ProjectileCount`. Each lane tells its own `SMSG_PROJECTILE_IMPACT`; a unit is hit by one lane at most, so a lane that
  meets a unit another lane already hit flies through it.
- **An explosion** (`ExplosionRadius` above 0). When a projectile ends on a unit, after that unit's impact and damage,
  one more `SMSG_ABILITY_FIRED` arrives under the projectile's `CastId` with a `Circle` footprint of the explosion's
  radius centred where the projectile met the unit; draw the burst from it. Each other unit it hits takes an ordinary
  damage packet. Nothing explodes at a wall or at the end of the projectile's reach.
- **A second hit** (`SecondHitEffectiveness` above 0). Each unit is hit, or healed, twice at once: two damage packets
  (or two `SMSG_UNIT_HEALED`), the second for its share, each with its own `Result`, so the second may crit, be blocked
  or be evaded on its own. A first hit that kills sends no second number on the corpse.
- **Longer and deeper auras** (`AuraDurationPct`, `AuraStacksBonus`). An aura entry's `DurationMs` is the placed,
  lengthened duration, so a shaped aura may last longer than its catalog row, and a periodic one ticks more times,
  each tick the same size; draw the timer from the entry. A shaped caster's stacking aura may hold more stacks than the
  row's `MaxStacks` ([aura protocol](aura-protocol.md#9-seeded-auras)). Crowd control is never lengthened or deepened.

**Keystones.** A keystone sits inside the hub ring between two hub junctions. It bends a rule (`keystone`), trades a
gain for a loss with `More` stat lines, or both:

| Node | Name | `keystone` | Stat lines |
|---:|---|---|---|
| 332 | Blood Price | 1, life cost: an ability's cost is paid from current life, never leaving less than 1 | – |
| 333 | Glass Edge | null | 30 % more damage, 25 % less maximum life |
| 334 | Steady Hand | 2, no crits: no hit, heal or aura tick of the holder's is critical | 20 % more damage, 20 % more healing |
| 335 | Unbroken | 3, ward takes all: the ward also absorbs blight and bleeding, and waits twice as long to recharge | – |

A keystone's stat lines reach the client as any node's do, through the character sheet. What the client sees of each
rule:

- **Blood Price.** Every cost shown in mana (`AbilityInfo` `Cost`) is paid from life instead: show it as life. A cast
  takes the cost off current health, which arrives through state replication as any health change, with no damage
  packet; mana does not move. A cast whose cost would leave less than 1 life is refused `NotEnoughPower` (the same
  `SAbilityNotReadyPacket` as too little mana): show it as not enough life. The accepted cast enters combat as any
  cast does.
- **Steady Hand.** The character sheet's crit chance is 0, and no hit, heal or aura tick of the holder's carries
  `Crit`.
- **Unbroken.** The holder's ward also takes blight hits, blight ticks and bleed ticks (their `WardAbsorbed` is set
  where it was 0), and starts recharging after twice the usual delay.
