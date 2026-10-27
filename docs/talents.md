# Talents

The shared talent tree (#1181): one wheel of nodes every class shares. Each class starts on its own node of the wheel,
which grants its single-target starter ability, and every other class ability is a node any class can walk to. This
page covers the tree as data (Part B1): its tables, the seeded first region, the world's catalog, the client export and
the public read. Characters do not hold nodes yet; Part B2 adds them (see [What later parts add](#what-later-parts-add)).

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
  kind. Characters will store the ids they hold (Part B2), so an id never changes, and a node added later takes the
  next id above the highest ever used.

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

## What later parts add

- **Part B2, characters on the tree.** A character's allocation (`CharacterTalents`), points earned per level and from
  the paying quests, the select check against the live tree, abilities derived from the class start and the held
  ability nodes (replacing `StartingSpells`, `CharacterAbilities` and `AbilityTemplates.AllowedClasses`), the tree's
  stat lines folded into the character's stats, `CMSG_TALENT_ALLOCATE` with `SMSG_TALENT_RESULT` and
  `SMSG_TALENT_STATE`, and the REST character reads.
- **Part C, the Lectern and refunds.** Refunding allocated nodes for gold at the Lectern.
- **Part D, capstones and keystones.** The capstone and keystone rows (ids from 306), their fork groups, the
  `Keystone` column and `TalentAbilityModifiers`.
