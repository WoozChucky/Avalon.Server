# Talents

The shared talent tree (#1181): one wheel of nodes every class shares. Each class starts on its own node of the wheel,
which grants its single-target starter ability, and every other class ability is a node any class can walk to. This
page covers the tree as data (Part B1): its tables, the seeded first region, the world's catalog, the client export and
the public read; and characters on the tree (Part B2): saved talents, points, the select check, abilities from the
tree, allocation, the talent state, the stats fold and the REST reads. The client's view is the
[talent protocol](talent-protocol.md).

## The tables

Six World tables, seeded by the World migration `AddTalentTree` from `TalentSeed`
(`src/Server/Avalon.Database.World/Seeding/TalentSeed.cs`) and configured in `WorldDbContext` beside the item modifier
catalog. The domain classes are in `src/Shared/Avalon.Domain/World/Talents.cs`, with no navigations. All six are
balance tables: they change only through `HasData` plus a generated migration (CLAUDE.md, `ModelDriftShould`).

| Table | Columns and keys | Checks and keys |
|---|---|---|
| `TalentNodes` | `Id` (`integer` key, never generated); `Kind` (`TalentNodeKind`, `smallint`); `Name` (at most 40, nullable); `Angle` and `Radius` (`real`); `ForkGroupId` (nullable) | `CK_TalentNodes_Kind` 1 to 7; `CK_TalentNodes_Name`: a notable, an ability, a capstone and a keystone are named; `CK_TalentNodes_Angle` in (-180, 180]; `CK_TalentNodes_Radius` finite and 0 or more; `CK_TalentNodes_ForkGroup`: only a capstone sits in a fork; `ForkGroupId` a `Restrict` foreign key to `TalentForkGroups` |
| `TalentEdges` | `NodeA`, `NodeB`, key both | `CK_TalentEdges_Order` `NodeA < NodeB`: each undirected edge once, the smaller id first; both `Restrict` foreign keys to `TalentNodes` |
| `TalentNodeStats` | `NodeId`, `Stat` (`StatType`, `smallint`), `Kind` (`TalentStatKind`, `smallint`), `Value`, `ValueMax` (nullable); key `(NodeId, Stat, Kind)` | `CK_TalentNodeStats_Kind` 1 or 2; `CK_TalentNodeStats_Range` `ValueMax` null or at least `Value`; `Cascade` from the node |
| `TalentNodeAbilities` | `NodeId` (key), `AbilityId` | unique index on `AbilityId` (an ability is granted by one node at most); `Cascade` from the node; `Restrict` to `AbilityTemplates`, so a linked ability cannot be deleted |
| `TalentForkGroups` | `Id` (key, never generated) | empty until Part D |
| `TalentClassStarts` | `Class` (`CharacterClass`, `smallint`, key), `NodeId` | `CK_TalentClassStarts_Class` 1 to 4; unique index on `NodeId`; `Restrict` to `TalentNodes` |

- **`TalentNodeKind`** (stored, append-only): `Start` 1 (a class's start: free, never allocated, linked to the class's
  starter), `Small` 2, `Notable` 3, `Ability` 4 (unlocks the ability its link names), `Capstone` 5, `Keystone` 6,
  `Junction` 7 (a hub junction, where a class's spoke meets the hub ring).
- **`TalentStatKind`** (stored, append-only): `Added` 1 (summed with the gear's lines of the same stat, as a worn item's
  global line is) and `More` 2 (a multiplier of its own, "N % more").
- **Stat lines** speak the gear's `StatType` vocabulary. Three stats arrived for the tree: `IncreasedArmor` 56,
  `IncreasedEvasion` 57 and `IncreasedWard` 58 (the global increases of each defence, also on the API contract's
  `StatType` and the wire's `ItemStat`).
- **The bundle.** `TalentTreeRows` (Domain) holds the six tables' rows. The seed (`TalentSeed.Rows()`), the repository
  (`ITalentTreeRepository.GetTreeAsync`, each table by its key), the catalog, the export and the API all share it.
- Ids are plain surrogates. No code finds a node by its id's value: a node is reached by a class start, a link or a
  kind. Characters store the ids they hold (`CharacterTalents`), so an id never changes, and a node added later takes
  the next id above the highest ever used.

## The seed

`TalentSeed` is the first region of the wheel, without its capstones and keystones: 305 nodes, 345 edges, 286 stat
lines, 23 ability links and four class starts. The model's `HasData`, the world's no-repository fallback, the export
and the tests read it.

- **Ids**, literal, in the design's table order: the Warrior's region 1-73 (start 1), the Hunter's 74-146 (start 74),
  the Wizard's 147-219 (start 147), the Cleric's 220-292 (start 220), and the shared nodes 293-305: the four joints
  (`WH-joint` 293, `HZ-joint` 294, `ZC-joint` 295, `CW-joint` 296), Frostbolt's branch 297-301 and the hub junctions
  302-305 (`hub-W`, `hub-H`, `hub-Z`, `hub-C`).
- **Labels are source-only keys.** Each row carries the design's label, for example `Ability(6, "WH-A5", "Cleave", Cleave)`:
  `WH-A5` is the Warrior's half-path toward the Hunter, lane A, node 5; `.c1` a node of the branch hanging
  off it; `Whub-L2` the Warrior's spoke, left route, node 2. A label wires the edges (written as chains of labels),
  places the node on the wheel and tells the reader which node a row is. It is never stored, exported or sent.
- **One region** (73 nodes, 84 edges): the start; two half-paths, one toward each neighbouring class, each two lanes
  (A and B) of ten nodes running from the start to the joint the two classes share, the lanes crossing at nodes 3, 6
  and 9; four branches of five small nodes, each hanging off an ability node; and a spoke toward the hub, `S1`-`S6`
  with a left route `L1`-`L3` and a right route `R1`-`R3` between `S3` and `S4`, ending on the class's hub junction.
  The four hub junctions form a ring. Frostbolt, the joint between the Hunter and the Wizard, carries the fifth shared
  branch.
- **Kinds**: 4 starts, 253 small nodes, 25 notables, 19 ability nodes and 4 junctions. Starts keep their names
  ("Warrior start"), notables and abilities theirs, and small nodes and junctions have none.
- **Stat lines**: every line is `Added`. A small node carries one line, a notable one or two, a junction one
  (`IncreasedMaximumLife` 3). The one ranged line is `Frostfletched`'s "attacks add 2 to 4 cold damage"
  (`AddedColdDamageToAttacks`, `Value` 2, `ValueMax` 4).
- **Ability links**: each start links its class's starter, and each ability node the ability its name names.

  | Node | Label | Ability | | Node | Label | Ability |
  |---:|---|---|---|---:|---|---|
  | 1 | `W-start` | 319 Heavy Strike | | 147 | `Z-start` | 210 Spark Bolt |
  | 6 | `WH-A5` | 200 Cleave | | 152 | `ZH-A5` | 212 Frost Fan |
  | 21 | `WH-B10` | 203 Rend | | 167 | `ZH-B10` | 214 Frost Bind |
  | 36 | `WC-A5` | 204 Shield Bash | | 182 | `ZC-A5` | 211 Flame Burst |
  | 51 | `WC-B10` | 201 Ground Slam | | 197 | `ZC-B10` | 213 Ignite |
  | 61 | `Whub-L2` | 202 Hurled Axe | | 220 | `C-start` | 230 Smite |
  | 74 | `H-start` | 220 Quick Shot | | 225 | `CZ-A5` | 231 Radiant Pulse |
  | 79 | `HW-A5` | 222 Scatter Shot | | 240 | `CZ-B10` | 235 Hush |
  | 94 | `HW-B10` | 221 Piercing Arrow | | 250 | `CW-A5` | 232 Mending Circle |
  | 109 | `HZ-A5` | 223 Crippling Shot | | 265 | `CW-B10` | 233 Renew |
  | 124 | `HZ-B10` | 224 Binding Shot | | 280 | `Chub-L2` | 234 Fortify |
  | | | | | 294 | `HZ-joint` | 320 Frostbolt |

  Heavy Strike and Frostbolt are new with the tree ([spell system](spell-system.md), §8).
- **Layout.** Angles in degrees, in (-180, 180], normalised by ±360; radii in layout units. A class's angle θ is -90
  for the Warrior, 0 for the Hunter, 90 for the Wizard and 180 for the Cleric. d is +1 on the half-paths W→H, H→Z,
  Z→C and C→W, and -1 on the others.

  | Node | Angle | Radius |
  |---|---|---|
  | start | θ | 600 |
  | lane node n (1-10) | θ + 4·d·n | 640 (lane A), 560 (lane B) |
  | joint `XY-joint` | θ(X) + 45 | 600 |
  | lane A branch node k | its ability's angle | 640 + 40k |
  | lane B branch node k | its ability's angle | 560 − 40k |
  | joint branch node k | the joint's angle | 600 + 40k |
  | spoke `S1`-`S3` | θ | 600 − 40n |
  | spoke `L1`-`L3` | θ − 4 | 480 − 40n |
  | spoke `R1`-`R3` | θ + 4 | 480 − 40n |
  | spoke `S4`-`S6` | θ | 360 − 40(n − 3) |
  | spoke branch `Xhub-L2.ck` | θ − 4 − 4k | 400 |
  | hub junction | θ | 160 |

  Every first-region position is distinct.
- **Quest talent points** are data in the same migration: `QuestTemplates.RewardTalentPoints`, 1 on quests 3, 7 and
  8, and refused on a repeatable quest ([quests](quests.md)).

## The catalog

`TalentCatalog` (`src/Server/Avalon.World/Talents/TalentCatalog.cs`) is the tree checked whole and immutable
afterwards.

- **Where it loads.** The Abilities reload area reads the tree rows first, builds the ability catalog, then builds
  the talent catalog against that ability catalog, off the tick, and publishes both together
  (`AbilitiesPatch(Catalog, Talents)`). So `/reload abilities` (and an ability template edit's reload) re-reads the
  tree, and a link is never checked against another generation's abilities. There is no `/reload talents`.
  `StaticData.Talents` is `TalentCatalog.Empty` before the first load. A `StaticData` built without a talent
  repository (tests) reads `TalentSeed.Rows()`. A reload reports `305 talent nodes`, or `talent tree refused: <reason>`.
- **The index API.** The nodes sit at dense indices 0 to `Count` − 1 in id order. `TryIndexOf` (a binary search) turns
  an id into an index; `NodeIdAt`, `KindAt`, `NeighboursAt` (ascending), `AbilityAt`, `LinesAt` (by stat, then kind; a
  line without a range has its value at both ends), `ForkPartnerAt` (-1 when none), `StartIndexOf` and `StarterOf`
  read by index or class. `WalkHeld(startIndex, held, reached, queue)` marks every held node a path of held nodes links
  to the start, over bit sets of `BitWords` words and a queue the caller owns, so a walk allocates nothing. On a refused
  or empty tree (`Count` 0, every start index -1) it reaches nothing: it clears `reached` and checks no argument.
- **What it refuses.** A tree that breaks a rule is refused whole, logged once at Error naming the first problem, in
  this order:
  1. a node listed twice; a class 1-4 without exactly one start; a start naming a class that is not playable, a
     missing node or a node that is not a `Start`; two classes starting on one node; a `Start` node that is no
     class's start;
  2. an edge naming a missing node;
  3. an ability link on a missing node or on a node that is neither `Start` nor `Ability`; a `Start` or `Ability` node
     without exactly one link; one ability linked by two nodes; a link naming an ability the ability catalog refused
     or does not hold;
  4. a node naming a missing fork group; a fork group that does not hold exactly two capstones and nothing else;
  5. a stat line naming a missing node, or one `TalentStatRules.Problem` refuses;
  6. a notable, ability, capstone or keystone without a name;
  7. a node no walk from the first class's start (the Warrior's) reaches.
- **A refused tree** holds no nodes (`Count` 0, `IsLoaded` false, `Refusal` the reason). It keeps `StarterOf` for each
  class whose start row names a `Start` node linked to an ability the ability catalog holds, so a bad deploy leaves
  every character its starter.
- **`TalentStatRules`** (`src/Server/Avalon.Combat/TalentStatRules.cs`, pure, shared with the tools that read the seed):
  - `Added` is allowed on the four attributes, `Armor`, `Evasion`, `Ward`, `Health`, `Power`, `BlockPct`, `CritPct`,
    `CritMultiplier`, the four speeds (`AttackSpeed`, `CastSpeed`, `CooldownRecovery`, `MovementSpeed`), the four
    resistances, the thirteen increases `IncreasedDamage` to `IncreasedHealing`, `IncreasedMaximumLife`,
    `IncreasedMaximumMana`, `IncreasedManaRegeneration`, `IncreasedArmor`, `IncreasedEvasion`, `IncreasedWard`, and
    the global added damage lines (to attacks by type, to spells by element);
  - `More` only on the thirteen offence increases and on increased maximum life and mana;
  - a `ValueMax` only on an added damage line, and at least its `Value`;
  - the stats local to an item (35-44) never, since a node has no item to scale.

## The export and the public read

- **The client export.** `dotnet run --project tools/Avalon.Exporter -- talent-catalog` writes
  `schema/talents/talent-catalog-v1.json` (`TalentCatalogExport`) from `TalentSeed.Rows()`, so it needs no database:
  `nodes` in id order (`id`, `kind`, `name`, `angle`, `radius`, `forkGroupId`, `abilityId`, and `stats` by stat then
  kind: `stat`, `kind`, `value`, `valueMax`), `edges` by `(a, b)` with `a < b`, `forkGroups` and `classStarts`
  (`class`, `nodeId`). Enumerations are numbers, listed in its `$comment`; camelCase, indented, `\n`.
  `TalentCatalogExportShould` fails when the committed file differs from the seed rendered, so a seed change needs a
  re-export ([tooling](tooling.md)).
- **The public read.** `GET /public/world/{worldId}/talents` (`PublicController.GetTalentTree`, `GetPublicTalentTree`,
  anonymous, `[PublicWorldScoped]`, the tooltips' cache headers) returns `TalentTreeDto`: the world's tree as its World
  database holds it (`TalentMapping.ToDto`), whether or not the world's catalog accepted it. Every field is a primitive
  or a contract enum (`TalentNodeKind`, `TalentStatKind`, `StatType`, `CharacterClass`)
  ([multi-world API](api-worlds.md#public-routes)).

## Saved talents

- **The rows.** `CharacterTalents` (Character database, `CharacterTalent { CharacterId, NodeId }`, `NodeId` an
  `integer`): key `(CharacterId, NodeId)`, `Cascade` from `Characters`, no foreign key to the World database (as
  `ItemInstance.TemplateId`). The Character migration `AddCharacterTalents` created it empty; `DropCharacterAbilities`
  dropped the per-character ability rows once nothing read them. The empty table is the release reset: every character
  starts the tree with its starter alone and every point to spend, which is also the free respec.
- **In memory.** `TalentAllocation` (`src/Server/Avalon.World/Talents/TalentAllocation.cs`) is World-side, on the
  concrete `CharacterEntity` as `Talents`, never on `ICharacter`, so no mod can read or change it. It holds the held
  node ids as a list kept ascending, with membership by binary search (no set; nothing allocated by a lookup or a
  walk), and the held lines summed into one `TalentTotals` buffer, rebuilt in place by `RebuildTotals` after a change,
  never per tick or per refresh. Memory is authoritative for a character in the world: its talents change only through
  `TalentAllocation` (`Load` at select, `Allocate`, `Drop`), whose mutators assert the tick thread while
  `TickThreadGuard` is enabled.
- **The save.** `Allocate` and `Drop` mark each node changed for the save (`SaveStateTracker.TalentChanged`, keyed by
  node id like an ignore); `Load` marks nothing. The snapshot rewrites every marked node from memory
  (`CharacterTalentWrite(Rewrite, Insert)`): the repository deletes the rewritten nodes' rows and inserts again the ones
  still held, in the save's one transaction ([inventory and saves](inventory-and-saves.md)), so a retry writes the same
  rows.

## Points

`TalentPoints.Earned(level, quests, catalog)` is the character's level plus the `RewardTalentPoints` of every quest in
its completions that the quest catalog holds; a repeatable quest pays none (the catalog refuses one that would). It
walks the completions' struct enumerator and allocates nothing. The points spent are the nodes held
(`TalentAllocation.Spent`); what may still be spent is earned minus spent. Nothing stores a balance: the world and the
API work the same figures out from the rows. Spent exceeds earned when earned falls while the nodes are kept: a reload
or a deploy refuses or removes a paying quest the character turned in, or lowers what it pays, or staff lower the level
(a REST character patch, read at the next select). Nothing drops for it in the world; the next select with the tree
loaded clears the whole tree (below), and while the tree is refused it lasts.

## Select

`CharacterSelectHandler` ([login flow](character-login-flow.md)), after the item instances are loaded: one step reads
the talent rows and the quest rows together, off the tick (`CharacterQuestRows.None` when the handler has no quest
repository). Back on the tick:

1. the quest log loads, first, since the points earned count the paying quests;
2. the talents load, and `TalentSelectCheck.Apply` drops, free, each drop marked for the save, what no longer fits the
   live tree, in order: a node the tree no longer holds, or a `Start`; then a held node no path of held nodes links to
   the class's start (`TalentCatalog.WalkHeld` over bit sets rented from the shared pool); then, if the nodes still
   held cost more than the points earned, every node, **whatever lowered the points** (owner decision): a paying quest
   the quest catalog refused, or a reload removed, clears the whole tree of a character at full spend as a lowered
   level does, and the drop is saved. Each step that drops anything logs one Warning naming the character id, with the
   count and the ids. **A tree the catalog refused drops nothing**: the rows stay as they are, inert, so a bad deploy
   of the tree wipes nobody's allocation. That promise covers a refused tree only, not refused quests. The held lines
   are then summed (`RebuildTotals`);
3. the stats refresh, with the tree folded (below), `EnterWorld`, and the bare-hand fallback when there is no class
   row;
4. the inventory snapshot;
5. `TalentAbilities.Rebuild` and `SMSG_CHARACTER_ABILITIES` (`CharacterAbilitiesSender`);
6. `SMSG_TALENT_STATE`, sent directly (the flusher reaches only spawned characters), and the level it was sent at
   recorded;
7. the ignore list, then the saved auras, as before.

## Abilities from the tree

`TalentAbilities.Rebuild` (World/Talents) builds the abilities a character holds: its class start's starter first,
then the ability each held ability node links, by ascending node id. Nothing else grants one: creation writes no
ability, and `CharacterCreateInfos.StartingSpells`, `AbilityTemplates.AllowedClasses` and the `CharacterAbilities`
table are gone ([spell system](spell-system.md), §8). An ability already held keeps its `IAbility`, and with it its
cooldown and cast timers; a new one is a `GameAbility` that arrives ready. An ability the catalog does not hold (it
refused it, or a reload removed it) is left out with a Warning. When the ids held are the same, in the same order,
nothing is built and nothing is allocated, and `Rebuild` returns false. Otherwise the list is rebuilt in place on the
World-side `CharacterAbilityContainer`: `StartRebuild` hands out its spare list, `FinishRebuild` swaps it with the
held one and parks every ability that left, and a later rebuild takes a parked ability back with `Unpark`, reset as a
new one (cooldown 0, its cast time, not casting), so a change allocates nothing once warm, however often a character
allocates and refunds. Parking is safe because a cast holds its own copy (`GameAbility.CloneForCast`), never the
character's ability. The parked list holds at most the abilities the tree grants and is cleared when the ability
catalog generation changes (`/reload abilities`). The modding API's `ICharacterAbilities` has none of these, so no mod
can grant an ability.

**Creation** writes no ability: a new character holds no node, so it enters with its starter alone. Its starting items
are `CharacterCreateItems` rows (class, Bag slot, template), which replaced the packed
`CharacterCreateInfos.StartingItems` in `AbilitiesFromTheTree`, the World migration that also dropped
`CharacterCreateInfos.StartingSpells` and `AbilityTemplates.AllowedClasses` and made Cleave an ability node (not
`Basic`, 6 mana). Dropping `AllowedClasses` changed every ability template's version once, so an editor holding an
older one gets a 409 on its next `PUT` ([live template editing](live-template-editing.md)).

A class ability is one the tree grants: `AbilityRules.Problem(t, granted)` and the aura rules hold an ability a class
start or a node links to the class rules, the world's `AbilityCatalog` being given the tree's link ids
([spell system](spell-system.md), §2).

## Allocation

`CMSG_TALENT_ALLOCATE` (`0x20E0`, `CTalentAllocatePacket { RequestId, NodeIds }`, a `MapSessionFilter` entry) is
handled by `TalentAllocateHandler` in the map pass, all or nothing (the [talent protocol](talent-protocol.md) has the
client's view):

- **The rules.** `TalentRules.Allocate(talents, tree, class, dead, earned, nodeIds, out refusedNode)` only decides: it
  changes nothing, marks no save and allocates nothing. In order: `Dead`; `TooMany` (more ids than earned minus spent,
  or than the tree holds, so every non-empty request while the tree is refused); then for each id in the order sent,
  `UnknownNode` (0, past `int.MaxValue`, or not in the tree), `AlreadyAllocated` (held, named earlier, or a `Start`),
  `NotConnected` (no neighbour that is the class's start, a held node or a node named earlier) and `ForkLocked` (its
  fork partner held or named earlier). A refusal names its node. "Named earlier" is one bit per tree index in an array
  rented from the shared pool, set as each id passes, so a request costs its length times a node's degree
  ([benchmarks](benchmarks.md), `Decide_refused_long`) and allocates nothing once the pool is warm. An empty list is
  `Ok` and changes nothing.
- **On `Ok`.** `TalentAllocation.Allocate` checks every id before it changes anything, holds each node and marks it for
  the save; then `TalentChange.Apply` (World/Talents): the totals rebuilt, `TalentAbilities.Rebuild`, the stats
  refreshed with `KeepShare` (a Warning when there is no class row), the ability list marked owed
  (`TalentAllocation.AbilitiesOwed`) only when the abilities held changed, and the talent state marked owed. The tick's
  `TalentFlusher` sends the list (`CharacterAbilitiesSender`, its amounts the stats as they stand then), then the
  state, once per tick however many changes the tick made, after every request's result; the amounts the list records
  keep `AbilityAmountsFlusher`, next, from sending them again. Both are marked in a `finally`: when the refresh throws
  after the abilities were rebuilt, the new list still reaches the client, so it never lacks an ability the server lets
  it cast, and the window still learns what is held.
- **The reply.** Every request is answered with exactly one `SMSG_TALENT_RESULT` (`0x30F0`, `STalentResultPacket`
  over the encoder's scratch packet); a connection with no character gets nothing. An exception is logged at Error and
  answered `InternalError`. `TalentResult` (`Ok` 0, `Dead` 1, `TooMany` 2, `UnknownNode` 3, `AlreadyAllocated` 4,
  `NotConnected` 5, `ForkLocked` 6, `InternalError` 7) is append-only.

## The talent state

`SMSG_TALENT_STATE` (`0x30F1`, `STalentStatePacket { NodeIds, PointsEarned, PointsSpent }`) is what the window shows,
whole every time. `Create` refills one id list per thread, so a send allocates nothing once the list has grown.

`TalentFlusher.Flush(connection, quests)` (World/Characters) runs once per tick for each connection, in the post-update
`ability_amounts` stage, after `CharacterSheetFlusher` and before `AbilityAmountsFlusher`
([instrumentation](instrumentation.md)). It reads two flags and one level. First, when the ability list is owed (a
talent change changed the abilities held), it sends `SMSG_CHARACTER_ABILITIES` and clears that flag. Then it sends the
state when the state is owed (an accepted
allocation, or a turn-in of a quest that pays talent points, which also says "You gained 1 talent point.",
[quests](quests.md)) or when the character's level differs from the one the last state was sent at, which catches
every level-up (anything that grants experience: a kill, a quest, an item) without touching the experience award. It
then clears the flag and records the level. The quest catalog the points are priced by is read once per tick.

## The stats fold

`CharacterStatsCalculator.Calculate(..., talents)` and `CharacterOffence.Of(..., talents)` fold the held lines exactly
as they fold a worn item's global lines; a null `TalentTotals` folds nothing. `TalentTotals.Add` sums the flat stats
and sends every other allowed stat to the offence sums (`CharacterOffence.Add`); a `More` line multiplies an offence
key, or the maximum life or mana, by `1 + value / 100`. In `Calculate`:

1. the offence line sums and both added-damage ranges start at the tree's before the gear walk adds to them;
2. the tree's attributes join the gear's before the conversions;
3. flat life and flat mana add the tree's `Health` and `Power`;
4. increased maximum life and mana are the gear's plus the tree's;
5. the maximum health and mana are then multiplied by the tree's `More` factor (`LocalScaling.More`, half away from
   zero, the product first rounded to six decimals so 5 x 0.7 is 4, saturating);
6. armour is the gear's plus the tree's flat armour plus Strength's, that whole rating scaled by the gear's and the
   tree's `IncreasedArmor`; evasion and ward the same;
7. block, crit, movement, the resistances and increased mana regeneration add the tree's.

Auras fold after all of it. Gear's global `IncreasedArmor`, `IncreasedEvasion` and `IncreasedWard` lines count the
same way (none is seeded). The world folds at select and after every talent change; the saved `CharacterStats` row is
what the world wrote after folding, so the REST character sheet reads it as it is
([inventory and saves](inventory-and-saves.md)).

## REST reads of a character

Both reads take the rows **as last saved**: the world applies its login check (`TalentSelectCheck`) at the character's
next select, and the REST reads do not re-run it, nor the world catalogs' refusals. Until that select, a node cut off
from the start, or more nodes than the points earned, still show, and bad World DB data (an ability or a tree the
world's catalog refuses) can list an ability the world would not grant. Both read only the tree rows the saved nodes
need (`ITalentTreeRepository.GetHeldAsync`: those nodes, their stat lines, and the links of those nodes and of the
class's start), never the whole tree.

- **`GET /world/{worldId}/character/{id}/talents`** (`CharacterController.GetTalents`, `GetCharacterTalents`; the
  owner, or GameMaster and above, as `/abilities`) returns `CharacterTalentsDto { CharacterId, NodeIds, PointsEarned,
  PointsSpent }`: the saved rows less the nodes the tree no longer holds (the same filter as `/abilities`), spent as
  their count, and the points earned worked out as the world does: the level plus the `RewardTalentPoints` of the
  quests the character turned in, summed by the World database (`IQuestRepository.SumTalentPointsAsync` over
  `ICharacterQuestRepository.GetCompletedQuestIdsAsync`; a repeatable quest, or one the World database no longer
  holds, pays nothing).
- **`GET /world/{worldId}/character/{id}/abilities`** is derived from the tree: the class start's starter, then each
  held ability node's ability by ascending node id, from `CharacterTalents` and the World database's tree rows. Rows
  naming a node the tree lacks are ignored, as the world ignores them until its next select. Each ability's advertised
  amount is built from an attacker whose offence includes the held nodes' lines (`TalentTotals` passed to
  `CharacterOffence.Of`).
- The character sheet (`/stats`) reads the saved `CharacterStats` row, which already folds the tree.

## What Part C consumes

Part C (the Lectern, refunds and their price) ships in the same release and designs its own tasks. Part B gives it:

- **`TalentCatalog`'s index API:** `Count`, `TryIndexOf`, `NodeIdAt`, `KindAt`, `NeighboursAt`, `StartIndexOf`,
  `ForkPartnerAt`, `BitWords` and `WalkHeld(startIndex, held, reached, queue)`, for a would-disconnect walk over
  caller-owned bit sets.
- **On `TalentAllocation`:** `Held`, `Holds`, `Spent`, `Version`, `MarkStateOwed` and the save mark; Part C adds a
  `Refund` mirroring `Allocate`.
- **After a refund:** `TalentChange.Apply` and `TalentPoints.Earned`.
- **Wire and flush:** `TalentResult`, `STalentStatePacket` and `TalentFlusher`, to append to.
- **Free numbers:** the opcode `0x20E1` (`CMSG_TALENT_REFUND`), `TalentResult` from 8, and field 4 of the state
  packet.

## What later parts add

- **Part C, the Lectern and refunds.** Refunding allocated nodes for gold at the Lectern.
- **Part D, capstones and keystones.** The capstone and keystone rows (ids from 306), their fork groups, the
  `Keystone` column and `TalentAbilityModifiers`.
