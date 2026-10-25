# Item modifiers

An item template is a **base** (#1179): a kind of item with its own base-stat ranges (its defence, or a weapon's damage
and attack time), requirements and price. Each copy of a base carries its own **base stats** and **modifiers**, rolled
when the copy is made and kept on the copy as rows of the Character database (`ItemInstanceBaseStats`,
`ItemInstanceModifiers`, [inventory and saves](inventory-and-saves.md)). This page is the modifier catalog and the
bases: what can roll, where, at which item level and how likely. Its tables are generated from the seed and checked
against it by `ItemModifierDocShould`; the prose around them is hand-written.

## The catalog

Six World database tables, all balance tables (seeded with `HasData`, changed only by a migration, never live-edited;
`/reload items` reads them again with the templates):

- `ItemThemes`: the Resonance themes (#1180), an `Id` and a unique `Name`, seeded with eight (see
  [Resonance themes](#resonance-themes)).
- `ItemModifierGroups`: one row per **exclusive group**. An item never carries two modifiers of one group. A group is an
  affix family (`Code` such as `P8` or `S13`, and `Element` for a family split by element), an implicit stat (shared by
  every base whose implicit gives it), or a unique's own modifier. Kind and name live here only, and so does an affix
  family's Resonance theme (`ThemeId`, a foreign key to `ItemThemes`).
- `ItemModifiers`: one row per tier of an affix or implicit group (`Tier` 1-9, counting down, and `RequiredItemLevel`),
  or a unique's single row with no tier.
- `ItemModifierStats`: a row's stat lines, each a range (`Min`-`Max`), and for an "adds X to Y" line a second range
  (`Min2`-`Max2`) for its high end. A row may have several lines.
- `ItemModifierWeights`: how likely a prefix or suffix row is on each base kind. No row, or weight 0, means it never
  rolls there.
- `ItemTemplateModifiers`: the modifiers every copy of a template rolls when it is made: a base's one implicit row (its
  implicit group at the base's tier), or a unique's own modifier.

Ids are surrogate numbers that mean nothing. Each group and each row has its id written as a literal in the seed table
(`ItemModifierSeed`); none is computed from where a row sits. An item names a catalog row by `ModifierId` (`integer`), so
an id never changes once seeded: a row added later takes the next free id, and `SeedIntegrityShould` holds every row
to the id committed in `tests/Avalon.Server.World.UnitTests/Seeding/item-modifier-ids.tsv` (a new row adds a line).
Nothing computes meaning from an id; a tier, a group or a kind is always read from its column.

`ItemModifierCatalog` loads the catalog for the world server and the REST API. It logs and leaves out a row with no stat
line, a prefix, suffix or implicit row without a tier or a unique row with one, a unique or implicit row that carries
a weight, a prefix or suffix row whose group has no theme or an implicit or unique row whose group has one (#1180),
and a template's links when they name a missing row, a prefix or a suffix, or mix a unique modifier with
anything else. Each template's links are also held against its rarity and kind (`ItemModifierCatalog.RarityRefusal`): a
unique template links exactly its one unique modifier, a common base exactly one implicit, and a template that is no
base (a consumable, a quest item) nothing. The world's items area (`ItemsPatch.Build`) logs and leaves out a template
whose links break this, whole, so no copy of it is ever made (a stat-less unique, a unique standing in its line as a
base, a base without its implicit), and a unique whose named base is missing, left out, not a common base of its kind or
above its level (`ItemBaseStatRules.NamedBaseProblem`); a unique template whose one modifier the catalog left out is kept, and
its drops are removed when they roll.

The REST API never reads the whole catalog. A tooltip or a link preview reads one template's links and the rows they
name, with their groups and stat lines and no weights (`IItemModifierRepository.GetLinkedToAsync`), held to the same
rules on that subset (but the weight one, which needs the weights; `SeedIntegrityShould` holds that no unique or
implicit carries one), and the template's links dropped when they break its rarity. A character's inventory reads only
the rows its items carry (`GetByIdsAsync`), for their kinds and their names. A live template edit that changes an
item's rarity is refused, keyed `rarity`, when the template's links would break it at the new rarity
([live template editing](live-template-editing.md)).

## Rarity

- **Common**: the base, its base stats and its implicit, nothing else.
- **Magic**: one or two affixes, at most one prefix and one suffix.
- **Rare**: three to six affixes, at most three prefixes and three suffixes.
- **Unique**: hand-made on a named base: the base's base stats and implicit, then its one unique modifier, every line a
  range each copy rolls in.

## Base stats

A base's defence and a weapon's numbers are not modifiers but its **base stats** (`ItemBaseStat`: `Armour`, `Evasion`,
`Ward`, `BlockChance`, `DamageMin`, `DamageMax` and `AttackTime`, in points, rating, points, percentage points, points,
points and milliseconds before attack speed). Each base authors a range per stat in the World table
`ItemTemplateBaseStats` (`TemplateId`, `Stat`, `Min`, `Max`; a balance table, loaded with its template), and each copy
rolls one value per stat, uniform within the range, when it is made, kept in the Character table
`ItemInstanceBaseStats` ([inventory and saves](inventory-and-saves.md)). A base carries exactly its kind's stats
(`ItemBaseStatRules.Of`): plate armour, leather evasion, cloth ward, a shield armour and block, a caster off-hand ward,
a sword, mace, staff or bow its damage range and attack time; a ring, an amulet or a trinket none. A weapon's template
names only the type of its damage (`ItemTemplates.DamageType`). A unique's template carries none of its own: it rolls
its base's ([implicits](#implicits)), and a unique weapon deals its base's damage type, so its template names none
either (the stats fold reads it through `WornItem.DamageType`, which the world's refresh and the API's worn read take
from the named base).

`ItemsPatch.Build` logs and leaves out, whole, a template that breaks `ItemBaseStatRules.Problem`: base stats other than
exactly its kind's, a range below 0 or with its minimum above its maximum, an attack time below 1 ms, a weapon whose
highest minimum damage passes its lowest maximum, or a damage type anywhere but on a Common weapon (or none on one; a
unique weapon with one of its own is refused too). A live template edit refuses the same, keyed `baseStats` or `damageType` ([live template editing](live-template-editing.md)).

The seeded ranges come from one drafted value per base and stat (`ItemBaseSeed.Drafted`), spread by 20 % each way, or
5 % for an attack time, each end rounded half away from zero and at least 1: a chest's armour 8 / 18 / 32 (plate),
evasion 40 / 90 / 160 (leather) and ward 12 / 26 / 45 (cloth) by tier, the other slots a share of it (legs 80 %, head
60 %, shoulders 50 %, hands and feet 40 %); a shield's armour 6 / 14 / 24 and block 5 / 7 / 10; a caster off-hand's ward
8 / 18 / 30; a weapon its kind's damage by tier and its pace (sword 700, bow 800, mace 900 and staff 1000 ms). Each
range is then its own row, retuned on its own; the Bases table below lists them all.

A copy's base stats are that item's own: the stats fold adds its armour, evasion and ward to the item's defence, which
the item's local "% increased" lines scale, and its block chance to block, unscaled; a main hand's damage range and
attack time are what an Attack rolls, which its local increased physical damage and attack speed lines scale ([inventory
and saves](inventory-and-saves.md), [the skill system](spell-system.md#4-hit-resolution)). An edit of a base's ranges
reaches only the copies made after it, never one already made. Base stats count toward no affix limit and are outside
the starting-zone budget.

## Implicits

Every base carries exactly one implicit, themed by its kind (plate leans on life, armour and toughness, leather on
evasion, speed and crit, cloth on ward, mana and spells; each weapon kind, shield, off-hand, ring line and amulet line
its own) and matched to its tier. The catalog has one implicit group per stat, 26 in all, each with rows T9, T8 and T7
whose ranges are the affix family's of the same stat at the same tier (P7's for increased projectile and physical
damage, which no family gives; maximum power its own); a base links the row of its own tier, required level 1, 5 and 10
being T9, T8 and T7. The generated tables below list every group and every base's implicit.

Every copy rolls its [base stats](#base-stats) and then its implicit, however it is made (a drop, a vendor, a quest, a
starting item). Implicits count toward neither affix limit and never block an affix: a resistance implicit and the same
resistance suffix sit on one piece, and the [starting-zone budget](#the-starting-zone-budget) counts both.

A copy made outright goes through `IItemRoller.RollPlain`: `IInventoryService.TryAdd` (a vendor purchase, an item
script's add, a quest reward that is not a base, a pickup of a drop that did not roll) and `CharacterCreateHandler` (a
starting item). A drop rolls its copy, its base stats and implicit first, when it drops ([drops](#drops)), and its pickup adds that
copy. Either way the rows enter the character's `ItemModifierStore` marked for the save that inserts the copy, or are
inserted with a new character.

A **unique** names its base (`ItemTemplates.BaseTemplateId`): a common base of its own kind at or under its required
level. Its copy rolls that base's base stats and implicit (ordinal 0), then its own modifier (ordinal 1); the unique's
template carries no base stats or implicit of its own. The trinket uniques are made on two trinket bases (150, 151) that
exist only for them: no loot table, vendor, quest or starting item hands one out.

## Rolling

A copy's **item level** caps the tiers it can roll: a row rolls only when its `RequiredItemLevel` is at most the item
level. Each affix is drawn by weight among the rows still **eligible**: weighing above 0 on the item's base kind, within
the item level, of a kind (prefix or suffix) with room left, and of a group the item does not carry yet. A row's chance is
its weight over the total weight of every eligible row, so lower tiers stay likelier than higher ones.

Every element of a split family (P8, P9, P10 and S5) is its own group with the full weights, so a rare may carry several
elements, up to three resistances.

## Drops

A creature's or a chest's drops roll when they drop, on the tick, in the kill or the use (`IItemRoller.RollDrops`):

- **Item level:** a creature's level; a chest's or a barrel's is drawn within its depth band, else the map's level range,
  else 1. The roll holds every item level at least 1 (`DropContext`), whatever its maker passes, since a copy at 0 could
  not be saved.
- **Line:** a line is the common bases of one kind that link the same implicit group (`ItemsPatch.LineOf`, `LinesOf`).
  A ring or an amulet drop, whose kind has several lines, first draws one of them uniformly, whichever line its loot
  entry names (the tables name the first tiers, the Copper Band and the Bone Charm); every other kind has one line and
  draws nothing. Only a drop draws: a quest reward or a named roll keeps its own line.
- **Base tier:** a base becomes the best tier of its line the item level reaches (item level 4 a T9 base, 5 the
  T8), and rolls that tier's base stats and implicit.
- **Rarity:** drawn by the creature's rarity (`Game:Items:Rarity`; a chest or a barrel by the Normal odds). A unique
  rolls its base's base stats and implicit, then its own modifier.
- **The boss's rare:** a Boss kill none of whose drops rolled Rare has its first drop that rolled a rarity rolled again,
  whole, as Rare. A unique is never touched.

The copy lies on the ground whole, its id, rarity, item level, base stats and modifiers included, and whoever picks it up gets
exactly that copy. A quest drop and any item that is not a base or a unique do not roll. A copy that rolled Magic, Rare
or Unique lies there, and is picked up, [unidentified](#identification); a Common one is identified.

The odds by the dropping creature's rarity are configuration (`Game:Items:Rarity`, [configuration
reference](configuration-reference.md)), checked at startup:

| Creature | Magic | Rare | Common (the rest) |
| --- | ---: | ---: | ---: |
| Normal (and every chest or barrel) | 22 % | 3 % | 75 % |
| Elite | 32 % | 8 % | 60 % |
| Rare | 40 % | 15 % | 45 % |
| Boss | 50 % | 50 % | 0 % |

## Quest rewards and vendors

- **A quest reward** that is a base rolls Magic at turn-in, once the room check has passed: its item level is the
  quest's level requirement (at least 1), its tier the best its own line reaches there, which the turn-in's Unique and room
  checks judge too, and the offer shows it as Magic with the tier it is paid as (`QuestRewardItemDto.Rarity` and
  `PaidItemTemplateId`, [quests](quests.md)), and arrives [unidentified](#identification). Any other reward is a plain
  copy, identified.
- **A vendor** sells only Common items (`VendorCatalog` refuses any other row, and a row whose item a `/reload items`
  made Unique since is hidden and refused, `VendorRules.IsSoldNew`), each a plain copy with its base stats and implicit. A
  sale pays the template's `SellPrice` times the copy's rarity factor (`Game:Items:SellFactor`: Magic 2, Rare 4,
  Unique 6, Common 1; [vendors](inventory-and-saves.md)).

## Weights

Every prefix and suffix row weighs **1000, 800 and 600** for T9, T8 and T7 on each base kind its group rolls on, and
has no weight anywhere else. The families' "Rolls on" column below is that table: "armour" is all eighteen armour kinds,
"plate", "leather" and "cloth" an armour type's six, and "helmets", "shoulders", "chests", "gloves", "legs" and "boots"
that slot in all three types. Trinkets roll nothing.

Each family rolls on a few fitting slots only, so a slot has an identity and a roll stays readable, and so that one
full set of the best level-10 items stays within the [starting-zone budget](#the-starting-zone-budget). A split family
may roll some of its elements on a slot and not others: each resistance rolls on one armour slot of its own (fire on
helmets, cold on boots, lightning on legs, blight on chests) besides the shield, the caster off-hand and the amulet.

## The starting-zone budget

The starting zone takes a character to about level 10. Every T9-T7 affix value, every implicit and every unique line
is sized so that one full best-in-slot level-10 set gives at most:

| Stat | Ceiling |
| --- | --- |
| Each resistance | 25 % |
| Attack speed (attack speed and local attack speed lines, and 0.1 % per point of Agility from gear) | 10 % |
| Cast speed | 10 % |
| Movement speed | 8 % |
| Crit chance | +6 % |
| Life | +75 flat (Stamina's life included) and +26 % increased: the guard checks each class's share, 40-60 % above its level-10 base |
| Strength, Agility, Intellect | +15 each |
| Crit multiplier | +50 % |
| Cooldown recovery | 15 % |
| Increased healing | 45 % |
| Block (a shield's base block chance and block lines) | 20 % |
| Maximum power (power lines, and the Wizard's power per Intellect and Agility from gear) | +35 % of a level-10 Wizard's pool |
| Added damage to attacks, each type, and to spells, each element (the average of an "adds" line's two ends) | 8.4 |

A set is counted at its worst case. Every slot (head, shoulders, chest, hands, legs, feet, amulet, two rings, two
trinkets, and either a one-handed weapon with an off-hand or a two-handed weapon, whichever gives more) holds whichever
level-10 item gives the stat most: a base rolled rare (its template's own stat lines, its implicit, plus each affix group
of the stat that rolls on its kind, at its best tier) or a unique (its template's lines, its base's implicit and its own
lines), every line at its maximum (an "adds" line's high end). Implicits and unique lines stack with an affix of the
same stat, so a slot can count twice. Attack speed reads the formula's `AttackSpeedPerAgility`. Life is (a class's
level-10 base + flat life + Stamina's life) x (1 + increased maximum life / 100), rounded as the stats fold rounds it,
each slot's flat and increased life taken from the one item it holds, at the set that gives most.
`SeedIntegrityShould.Fit_the_starting_zone_budget` works each of these totals out from the seed and fails when one
passes its ceiling, or when a class's life share leaves 40-60 %. The values were also sized to the design's ceilings
that need a model of a whole hit (increased damage on one hit summed 60 %, a weapon's own affixes +60 % of its average,
flat and increased defence +50 % of the set's base), which no test holds. Base stats are not part of the budget, but
for block, whose ceiling counts the shield's. The tables below give every value.

## Local and global

A **local** modifier applies to the item it sits on: added damage, increased physical damage and attack speed on a
weapon change that weapon's hits only, and an armour piece's flat or increased defence changes that piece's defence only.
Every other modifier is **global** and applies to the character, among them the added damage "to attacks" (gloves)
and "to spells" (rings).

## Tiers

Tiers count down: **T9 is the weakest and T1 the best.** Every family has T9, T8 and T7, the starting zone's, unlocking
at item level **1, 4 and 7**; T6 to T1 are kept for higher item levels and later content. Each tier beats the one below
at its best value (`SeedIntegrityShould.Beat_the_tier_below_at_its_best_value`). The fourth and fifth tiers of the
first catalog are gone (World migration `RevalueAffixesForTheStartingZone`), their ids never reused; its first three
tiers kept their ids as T9, T8 and T7.

An "adds X to Y" line names its two ends exactly: its low end X and its high end Y are seeded as fixed numbers (`Min` =
`Max` = X, `Min2` = `Max2` = Y), and every hit rolls between them. "adds 1" is 1 to 1.

## Naming

- A magic item is named `<prefix name> <base name> <suffix name>`, a missing part dropped. The name is worked out
  from the current items generation, so renaming a family or a base (a seed migration, then `/reload items`) renames every copy.
- A rare item gets a two-word name when it rolls, kept on the copy (`ItemInstances.RareName`), so editing the word lists
  never renames an item a player holds. The first word comes from one list, the second from a list that follows the
  kind: weapons (sword, mace, staff, bow), jewellery (amulet, ring, caster off-hand) or armour (armour pieces and
  shields); the lists are code (`RareNames`).
- Common and unique items show the template's name.
- An [unidentified](#identification) copy is shown by no name at all, only its template's.
- `ItemNames.Display` (`Avalon.Domain`) gives the name, or null where the template's name is the item's, from a magic
  copy's lowest-ordinal prefix and suffix. It runs when an identified copy is first described in an items generation,
  never per tick: `ItemSlotDto` field 11
  (the login snapshot, inventory updates, item replies, the bank, the buyback) and `TradeItemDto` field 8 send it as an
  optional string, absent for a common or unique copy and for every unidentified one, and the REST inventory read's
  item `name` likewise. A drop on the ground sends no name: every copy that would have one lies there unidentified.
  The world holds a magic copy's name as a field beside its modifiers (`DescribedName`, on the copy's entry in
  `ItemModifierStore`), keyed by the items generation it was worked out in, so describing the same copy again builds
  no string, a reload's rename reaches it at its next describe, and a copy whose modifiers change is a new entry. An
  unidentified copy's held name is neither read nor filled, so its first describe once identified works it out. A rare
  copy keeps its stored name even when a reload removed its template.
- A family has one name for all its tiers (the tier shows as a number), one per element on a split family. Implicits
  (a unique's inherited one too) and unique modifiers never name an item.

## Identification

A magic, rare or unique copy arrives **unidentified** when it drops or a quest pays it as a rolled reward; a common copy
is always identified, and so is every copy a vendor sells, an item script adds (a unique included) or a character
starts with. Until it is identified, a player sees the copy's rarity, base name, item level, base stats and implicit
line, and "Unidentified" where its affixes go: the server never sends its prefixes, its suffixes, a unique's own line
or a generated name, to any client and on any path, the REST inventory included and for every caller. An unidentified
unique shows its template's name, its base's base stats and its base's implicit, and not its own line. Which modifiers
show is decided by the catalog row's kind (`ItemVisibility`: implicit lines only, an id the catalog no longer holds
hidden), never by the order the copy holds them in.

An unidentified copy cannot be worn, but it can be kept, banked, traded and sold, for half what it would sell for
identified (`Game:Items:UnidentifiedSellPercent`). It is identified one item per request at the Resonance Workbench in
Glimmerdell's market, for `Game:Items:IdentifyCost` gold (0 today), which reveals its name and every modifier it
rolled when it was made: identifying rolls nothing. [Inventory and saves](inventory-and-saves.md#identification-at-the-resonance-workbench-1179)
has the request and its rules.

## Resonance themes

Every affix family belongs to one **Resonance theme** (#1180): Ember, Rime, Storm, Wild, Iron, Thorn, Gale or Veil,
rows of `ItemThemes` (the Themes table below lists each theme's families). The theme is the group's: `ThemeId` is set
on every prefix and suffix group, and on no implicit or unique group, so an implicit line or a unique's own line never
has a theme. Both the database (`CK_ItemModifierGroups_Theme`, `("Kind" IN (1, 2)) = ("ThemeId" IS NOT NULL)`) and the
catalog (which leaves out a row that breaks it) hold that rule, and `SeedIntegrityShould` holds each family to the
theme the seed gives it. A theme's id means nothing; nothing reads a theme's meaning from its number, and there is no
limit on how many themes the table holds.

**The count.** A character's **Resonance** for a theme is how many of its worn items carry at least one prefix or
suffix of that theme:

- only items worn in a slot that accepts them count, never one in the bags or the bank;
- an item counts once per theme it carries, however many of its affixes share that theme (two Thorn affixes on one
  sword count 1 for Thorn), and once for each different theme;
- only affixes count: an implicit and a unique's own line carry no theme.

The catalog builds a `ResonanceTable` once per load (`ItemModifierCatalog.Resonance`): the distinct themes its accepted
affix rows name, each given a slot, its position in that list sorted by id, and each affix modifier mapped to its
theme's slot. The stats refresh counts the worn items against it ([world simulation](world-simulation.md), the stats
fold), and the character sheet and the REST API show the counts by theme id
([inventory and saves](inventory-and-saves.md)). A theme no worn item carries is not listed.

**The effects (#1180 part A2).** A count turns on its theme's **Resonance effects**: rows of `ItemThemeEffects`, a
World DB balance table seeded from `ResonanceSeed`, which the Theme effects table below lists in full and which is the
reference for every number. Each row names its theme and the count it needs, a trigger (Hit, Crit, Kill, Struck,
Evaded or WardBroken), a chance, a cooldown, the aura it applies and on whom (the wearer, the unit it hit, or the unit
that struck it), and, where it applies, how many further creatures it chains to, the wearer's life it fires at or
below, and the damage type of an aura that deals damage. A theme runs only the rows of the highest count it reaches,
so a count of 5 runs the 4 rows, and a count of 1 runs nothing; the seed has rows at 2, 3 and 4. In words:

- **Ember** sets what its wearer hits Burning (fire damage over 4 s), more often and harder at each count.
- **Rime** chills what its wearer hits (slower movement, and from 3 slower attacks); at 4 a critical hit may also root
  it for 1 s (Rimebound).
- **Storm**: a critical hit jolts its target (one burst of lightning damage 0.2 s later); from 3 the jolt also jumps to
  the nearest creature within 8 m of the target, at 4 to the two nearest.
- **Wild**: a kill heals the wearer over 4 s (Regrowth); at 4 a hit taken at or below 35 % life also heals it over 3 s
  (Rootsurge), at most once in 20 s.
- **Iron**: a hit taken may raise the wearer's guard for 4 s (armour and block, and at 4 a ward).
- **Thorn**: a hit taken barbs the attacker (one burst of physical damage 0.2 s later); at 4 a critical hit makes its
  target bleed.
- **Gale**: a kill speeds the wearer up (movement, and from 3 attack and cast speed); at 4 an evaded hit does too
  (Slipstream).
- **Veil**: a broken ward veils the wearer (evasion, and from 3 movement), at most once in 12 s; at 4 a critical hit
  also quickens its casting (Lucid).

An effect's aura is an ordinary aura (ids 16-44, [auras](auras.md#resonance-effects-1180)): it ticks, stacks, breaks
and replicates as any other. How the effects are activated and fired, and what they cost on a hit, is in
[world simulation](world-simulation.md) (the Resonance effects).

## The tables

<!-- item-modifiers:begin -->
<!-- Generated by tools/Avalon.Exporter -- item-modifier-doc from ItemModifierSeed. Do not edit by hand. -->

### Themes

| Id | Theme | Families |
| --- | --- | --- |
| 1 | Ember | P2 P8 Fire P9 Fire P10 Fire S5 Fire |
| 2 | Rime | P3 P8 Cold P9 Cold P10 Cold S5 Cold |
| 3 | Storm | P4 P8 Lightning P9 Lightning P10 Lightning S5 Lightning |
| 4 | Wild | P5 P8 Blight P9 Blight P11 P18 P19 S4 S5 Blight S14 |
| 5 | Iron | P12 P13 S13 |
| 6 | Thorn | P1 P6 P9 Physical S1 S9 S10 |
| 7 | Gale | P14 P15 S2 S6 S7 S11 |
| 8 | Veil | P7 P16 P17 S3 S8 S12 |

### Theme effects

A theme runs the rows of the highest count it reaches. The effect is the aura it applies to the unit under On; a chain also applies it to that many further creatures within 8 m of the hit target.

| Theme | Count | Trigger | Chance | Cooldown | Effect | On | Chain | Life |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Ember | 2 | Hit | 20 % | 2 s | Burning I | HitTarget | - | - |
| Ember | 3 | Hit | 25 % | 2 s | Burning II | HitTarget | - | - |
| Ember | 4 | Hit | 30 % | 2 s | Burning III | HitTarget | - | - |
| Rime | 2 | Hit | 20 % | 3 s | Chilled I | HitTarget | - | - |
| Rime | 3 | Hit | 25 % | 3 s | Chilled II | HitTarget | - | - |
| Rime | 4 | Hit | 30 % | 3 s | Chilled III | HitTarget | - | - |
| Rime | 4 | Crit | 25 % | 10 s | Rimebound | HitTarget | - | - |
| Storm | 2 | Crit | 100 % | 2 s | Jolt I | HitTarget | - | - |
| Storm | 3 | Crit | 100 % | 2 s | Jolt II | HitTarget | 1 | - |
| Storm | 4 | Crit | 100 % | 2 s | Jolt III | HitTarget | 2 | - |
| Wild | 2 | Kill | 100 % | 4 s | Regrowth I | Wearer | - | - |
| Wild | 3 | Kill | 100 % | 4 s | Regrowth II | Wearer | - | - |
| Wild | 4 | Kill | 100 % | 4 s | Regrowth III | Wearer | - | - |
| Wild | 4 | Struck | 100 % | 20 s | Rootsurge | Wearer | - | ≤ 35 % |
| Iron | 2 | Struck | 20 % | 6 s | Guard I | Wearer | - | - |
| Iron | 3 | Struck | 25 % | 6 s | Guard II | Wearer | - | - |
| Iron | 4 | Struck | 30 % | 6 s | Guard III | Wearer | - | - |
| Thorn | 2 | Struck | 100 % | 2 s | Barbs I | Attacker | - | - |
| Thorn | 3 | Struck | 100 % | 2 s | Barbs II | Attacker | - | - |
| Thorn | 4 | Struck | 100 % | 2 s | Barbs III | Attacker | - | - |
| Thorn | 4 | Crit | 100 % | 4 s | Bleeding | HitTarget | - | - |
| Gale | 2 | Kill | 100 % | 5 s | Tailwind I | Wearer | - | - |
| Gale | 3 | Kill | 100 % | 5 s | Tailwind II | Wearer | - | - |
| Gale | 4 | Kill | 100 % | 5 s | Tailwind III | Wearer | - | - |
| Gale | 4 | Evaded | 100 % | 8 s | Slipstream | Wearer | - | - |
| Veil | 2 | WardBroken | 100 % | 12 s | Veiled I | Wearer | - | - |
| Veil | 3 | WardBroken | 100 % | 12 s | Veiled II | Wearer | - | - |
| Veil | 4 | WardBroken | 100 % | 12 s | Veiled III | Wearer | - | - |
| Veil | 4 | Crit | 100 % | 8 s | Lucid | Wearer | - | - |

### Families

| Code | Element | Name | Kind | Theme | Stat | Rolls on |
| --- | --- | --- | --- | --- | --- | --- |
| P1 |  | Briar-edged | Prefix | Thorn | AddedPhysicalDamage | Sword, Mace, Staff, Bow |
| P2 |  | Smouldering | Prefix | Ember | AddedFireDamage | Sword, Mace, Staff, Bow |
| P3 |  | Rimed | Prefix | Rime | AddedColdDamage | Sword, Mace, Staff, Bow |
| P4 |  | Crackling | Prefix | Storm | AddedLightningDamage | Sword, Mace, Staff, Bow |
| P5 |  | Festering | Prefix | Wild | AddedBlightDamage | Sword, Mace, Staff, Bow |
| P6 |  | Honed | Prefix | Thorn | LocalIncreasedPhysicalDamage | Sword, Mace, Staff, Bow |
| P7 |  | Whispering | Prefix | Veil | IncreasedSpellDamage | Mace, Staff, CasterOffHand |
| P8 | Fire | Ashen | Prefix | Ember | IncreasedFireDamage | shoulders, Staff, CasterOffHand, Amulet, Ring |
| P8 | Cold | Hoarfrost | Prefix | Rime | IncreasedColdDamage | shoulders, Staff, CasterOffHand, Amulet, Ring |
| P8 | Lightning | Galvanic | Prefix | Storm | IncreasedLightningDamage | shoulders, Staff, CasterOffHand, Amulet, Ring |
| P8 | Blight | Mouldering | Prefix | Wild | IncreasedBlightDamage | shoulders, Staff, CasterOffHand, Amulet, Ring |
| P9 | Physical | Thorn-tipped | Prefix | Thorn | AddedPhysicalDamageToAttacks | gloves |
| P9 | Fire | Thorn-tipped | Prefix | Ember | AddedFireDamageToAttacks | gloves |
| P9 | Cold | Thorn-tipped | Prefix | Rime | AddedColdDamageToAttacks | gloves |
| P9 | Lightning | Thorn-tipped | Prefix | Storm | AddedLightningDamageToAttacks | gloves |
| P9 | Blight | Thorn-tipped | Prefix | Wild | AddedBlightDamageToAttacks | gloves |
| P10 | Fire | Ember-sung | Prefix | Ember | AddedFireDamageToSpells | Ring |
| P10 | Cold | Ember-sung | Prefix | Rime | AddedColdDamageToSpells | Ring |
| P10 | Lightning | Ember-sung | Prefix | Storm | AddedLightningDamageToSpells | Ring |
| P11 |  | Hale | Prefix | Wild | Health | helmets, chests, legs, boots, Shield, Amulet |
| P12 |  | Barkbound | Prefix | Iron | Armor | plate, Shield |
| P13 |  | Ironbark | Prefix | Iron | LocalIncreasedArmor | plate, Shield |
| P14 |  | Fleet-footed | Prefix | Gale | Evasion | leather |
| P15 |  | Mistwoven | Prefix | Gale | LocalIncreasedEvasion | leather |
| P16 |  | Veilwoven | Prefix | Veil | Ward | cloth, CasterOffHand |
| P17 |  | Glamoured | Prefix | Veil | LocalIncreasedWard | cloth, CasterOffHand |
| P18 |  | Lingering | Prefix | Wild | IncreasedDamageOverTime | Staff, CasterOffHand, Amulet |
| P19 |  | Mending | Prefix | Wild | IncreasedHealing | Mace, Staff, CasterOffHand, Amulet |
| S1 |  | of the Ox-root | Suffix | Thorn | Strength | chests, Ring |
| S2 |  | of the Hare | Suffix | Gale | Agility | legs, Ring |
| S3 |  | of the Owl | Suffix | Veil | Intellect | shoulders, Ring |
| S4 |  | of the Old Oak | Suffix | Wild | Stamina | boots |
| S5 | Fire | of the Hearth | Suffix | Ember | FireResistance | helmets, Shield, CasterOffHand, Amulet |
| S5 | Cold | of the Thaw | Suffix | Rime | ColdResistance | boots, Shield, CasterOffHand, Amulet |
| S5 | Lightning | of Grounding | Suffix | Storm | LightningResistance | legs, Shield, CasterOffHand, Amulet |
| S5 | Blight | of the Clean Spring | Suffix | Wild | BlightResistance | chests, Shield, CasterOffHand, Amulet |
| S6 |  | of Haste | Suffix | Gale | LocalAttackSpeed | Sword, Mace, Staff, Bow |
| S7 |  | of Quick Hands | Suffix | Gale | AttackSpeed | gloves |
| S8 |  | of Cadence | Suffix | Veil | CastSpeed | gloves, Staff, CasterOffHand |
| S9 |  | of the Keen Eye | Suffix | Thorn | CritPct | gloves, Sword, Mace, Staff, Bow |
| S10 |  | of Ruin | Suffix | Thorn | CritMultiplier | helmets, gloves, Sword, Mace, Staff, Bow, CasterOffHand |
| S11 |  | of the Wanderer | Suffix | Gale | MovementSpeed | boots |
| S12 |  | of Renewal | Suffix | Veil | CooldownRecovery | helmets, shoulders |
| S13 |  | of the Bulwark | Suffix | Iron | BlockPct | Shield |
| S14 |  | of Deep Roots | Suffix | Wild | IncreasedMaximumLife | shoulders, chests, legs |

### Tiers

Each cell is the tier's range, then its weight on every base kind the family rolls on. An "adds" line reads as its low end, then "to" and its high end. Tiers count down: T9 is the weakest.

| Code | Element | T9 | T8 | T7 |
| --- | --- | --- | --- | --- |
| Item level | | 1 | 4 | 7 |
| P1 |  | 1 to 1 · 1000 | 1 to 2 · 800 | 2 to 3 · 600 |
| P2 |  | 1 to 1 · 1000 | 1 to 2 · 800 | 2 to 3 · 600 |
| P3 |  | 1 to 1 · 1000 | 1 to 2 · 800 | 2 to 3 · 600 |
| P4 |  | 1 to 2 · 1000 | 1 to 3 · 800 | 1 to 4 · 600 |
| P5 |  | 1 to 1 · 1000 | 1 to 2 · 800 | 2 to 3 · 600 |
| P6 |  | 5-8 · 1000 | 9-12 · 800 | 13-16 · 600 |
| P7 |  | 2-3 · 1000 | 3-4 · 800 | 5-6 · 600 |
| P8 | Fire | 1-2 · 1000 | 2-3 · 800 | 3-4 · 600 |
| P8 | Cold | 1-2 · 1000 | 2-3 · 800 | 3-4 · 600 |
| P8 | Lightning | 1-2 · 1000 | 2-3 · 800 | 3-4 · 600 |
| P8 | Blight | 1-2 · 1000 | 2-3 · 800 | 3-4 · 600 |
| P9 | Physical | 1 to 1 · 1000 | 1 to 2 · 800 | 1 to 3 · 600 |
| P9 | Fire | 1 to 1 · 1000 | 1 to 2 · 800 | 1 to 3 · 600 |
| P9 | Cold | 1 to 1 · 1000 | 1 to 2 · 800 | 1 to 3 · 600 |
| P9 | Lightning | 1 to 1 · 1000 | 1 to 2 · 800 | 1 to 3 · 600 |
| P9 | Blight | 1 to 1 · 1000 | 1 to 2 · 800 | 1 to 3 · 600 |
| P10 | Fire | 1 to 1 · 1000 | 1 to 2 · 800 | 1 to 3 · 600 |
| P10 | Cold | 1 to 1 · 1000 | 1 to 2 · 800 | 1 to 3 · 600 |
| P10 | Lightning | 1 to 1 · 1000 | 1 to 2 · 800 | 1 to 3 · 600 |
| P11 |  | 1-2 · 1000 | 3-4 · 800 | 5 · 600 |
| P12 |  | 1-2 · 1000 | 2-3 · 800 | 3-4 · 600 |
| P13 |  | 5-8 · 1000 | 9-12 · 800 | 13-16 · 600 |
| P14 |  | 5-8 · 1000 | 9-14 · 800 | 15-20 · 600 |
| P15 |  | 5-8 · 1000 | 9-12 · 800 | 13-16 · 600 |
| P16 |  | 1-2 · 1000 | 2-4 · 800 | 4-6 · 600 |
| P17 |  | 5-8 · 1000 | 9-12 · 800 | 13-16 · 600 |
| P18 |  | 1 · 1000 | 2 · 800 | 3 · 600 |
| P19 |  | 2-3 · 1000 | 3-4 · 800 | 5-6 · 600 |
| S1 |  | 1 · 1000 | 2 · 800 | 3 · 600 |
| S2 |  | 1 · 1000 | 2 · 800 | 3 · 600 |
| S3 |  | 1 · 1000 | 2 · 800 | 3 · 600 |
| S4 |  | 1 · 1000 | 2 · 800 | 3 · 600 |
| S5 | Fire | 1-2 · 1000 | 2-3 · 800 | 3-4 · 600 |
| S5 | Cold | 1-2 · 1000 | 2-3 · 800 | 3-4 · 600 |
| S5 | Lightning | 1-2 · 1000 | 2-3 · 800 | 3-4 · 600 |
| S5 | Blight | 1-2 · 1000 | 2-3 · 800 | 3-4 · 600 |
| S6 |  | 1 · 1000 | 2 · 800 | 3 · 600 |
| S7 |  | 1 · 1000 | 2 · 800 | 3 · 600 |
| S8 |  | 1 · 1000 | 2 · 800 | 3 · 600 |
| S9 |  | 1 · 1000 | 2 · 800 | 3 · 600 |
| S10 |  | 2-3 · 1000 | 4-5 · 800 | 5-6 · 600 |
| S11 |  | 2 · 1000 | 3 · 800 | 4 · 600 |
| S12 |  | 1 · 1000 | 2 · 800 | 3 · 600 |
| S13 |  | 1-2 · 1000 | 2-3 · 800 | 3-4 · 600 |
| S14 |  | 1-2 · 1000 | 2-3 · 800 | 3-4 · 600 |

### Implicits

Each implicit group gives one stat; a base links the row of its own tier. Its ranges are the source family's at the same tier, or its own.

| Group | Stat | Source | T9 | T8 | T7 |
| --- | --- | --- | --- | --- | --- |
| 111 | FireResistance | S5 | 1-2 | 2-3 | 3-4 |
| 112 | ColdResistance | S5 | 1-2 | 2-3 | 3-4 |
| 113 | LightningResistance | S5 | 1-2 | 2-3 | 3-4 |
| 114 | BlightResistance | S5 | 1-2 | 2-3 | 3-4 |
| 115 | LocalIncreasedArmor | P13 | 5-8 | 9-12 | 13-16 |
| 116 | IncreasedMaximumLife | S14 | 1-2 | 2-3 | 3-4 |
| 117 | AddedPhysicalDamageToAttacks | P9 | 1 to 1 | 1 to 2 | 1 to 3 |
| 118 | Strength | S1 | 1 | 2 | 3 |
| 119 | Stamina | S4 | 1 | 2 | 3 |
| 120 | CritMultiplier | S10 | 2-3 | 4-5 | 5-6 |
| 121 | Agility | S2 | 1 | 2 | 3 |
| 122 | LocalIncreasedEvasion | P15 | 5-8 | 9-12 | 13-16 |
| 123 | IncreasedProjectileDamage | P7 | 2-3 | 3-4 | 5-6 |
| 124 | MovementSpeed | S11 | 2 | 3 | 4 |
| 125 | CooldownRecovery | S12 | 1 | 2 | 3 |
| 126 | Intellect | S3 | 1 | 2 | 3 |
| 127 | IncreasedSpellDamage | P7 | 2-3 | 3-4 | 5-6 |
| 128 | CastSpeed | S8 | 1 | 2 | 3 |
| 129 | Power | own | 10-15 | 20-25 | 30-40 |
| 130 | Health | P11 | 1-2 | 3-4 | 5 |
| 131 | IncreasedPhysicalDamage | P7 | 2-3 | 3-4 | 5-6 |
| 132 | AddedLightningDamageToAttacks | P9 | 1 to 1 | 1 to 2 | 1 to 3 |
| 133 | AddedFireDamageToSpells | P10 | 1 to 1 | 1 to 2 | 1 to 3 |
| 134 | AddedColdDamageToSpells | P10 | 1 to 1 | 1 to 2 | 1 to 3 |
| 135 | IncreasedDamageOverTime | P18 | 1 | 2 | 3 |
| 136 | IncreasedHealing | P19 | 2-3 | 3-4 | 5-6 |

### Bases

Each kind's lines, by their first tier's id. A base's base stats are the ranges every copy rolls; its implicit is its line's group at the base's tier. A trinket base has no tier: only a unique is made on it.

| Kind | Template | Name | Tier | Level | Base stats | Implicit |
| --- | --- | --- | --- | --- | --- | --- |
| PlateHead | 36 | Ironbound Helm | T9 | 1 | Armour 4-6 | FireResistance 1-2 |
| PlateHead | 12 | Barkplate Helm | T8 | 5 | Armour 9-13 | FireResistance 2-3 |
| PlateHead | 85 | Ironroot Helm | T7 | 10 | Armour 15-23 | FireResistance 3-4 |
| PlateShoulders | 76 | Ironbound Pauldrons | T9 | 1 | Armour 3-5 | LocalIncreasedArmor 5-8 |
| PlateShoulders | 79 | Barkplate Pauldrons | T8 | 5 | Armour 7-11 | LocalIncreasedArmor 9-12 |
| PlateShoulders | 82 | Ironroot Pauldrons | T7 | 10 | Armour 13-19 | LocalIncreasedArmor 13-16 |
| PlateChest | 37 | Ironbound Chestguard | T9 | 1 | Armour 6-10 | IncreasedMaximumLife 1-2 |
| PlateChest | 13 | Barkplate Chestguard | T8 | 5 | Armour 14-22 | IncreasedMaximumLife 2-3 |
| PlateChest | 86 | Ironroot Chestguard | T7 | 10 | Armour 26-38 | IncreasedMaximumLife 3-4 |
| PlateHands | 39 | Ironbound Gauntlets | T9 | 1 | Armour 2-4 | AddedPhysicalDamageToAttacks 1 to 1 |
| PlateHands | 15 | Barkplate Gauntlets | T8 | 5 | Armour 6-8 | AddedPhysicalDamageToAttacks 1 to 2 |
| PlateHands | 88 | Ironroot Gauntlets | T7 | 10 | Armour 10-16 | AddedPhysicalDamageToAttacks 1 to 3 |
| PlateLegs | 38 | Ironbound Legguards | T9 | 1 | Armour 5-7 | Strength 1 |
| PlateLegs | 14 | Barkplate Legguards | T8 | 5 | Armour 11-17 | Strength 2 |
| PlateLegs | 87 | Ironroot Legguards | T7 | 10 | Armour 21-31 | Strength 3 |
| PlateFeet | 40 | Ironbound Boots | T9 | 1 | Armour 2-4 | Stamina 1 |
| PlateFeet | 16 | Barkplate Boots | T8 | 5 | Armour 6-8 | Stamina 2 |
| PlateFeet | 89 | Ironroot Boots | T7 | 10 | Armour 10-16 | Stamina 3 |
| LeatherHead | 46 | Hide Cap | T9 | 1 | Evasion 19-29 | CritMultiplier 2-3 |
| LeatherHead | 22 | Fernstalker Cap | T8 | 5 | Evasion 43-65 | CritMultiplier 4-5 |
| LeatherHead | 90 | Gloamhide Cap | T7 | 10 | Evasion 77-115 | CritMultiplier 5-6 |
| LeatherShoulders | 77 | Hide Mantle | T9 | 1 | Evasion 16-24 | Agility 1 |
| LeatherShoulders | 80 | Fernstalker Mantle | T8 | 5 | Evasion 36-54 | Agility 2 |
| LeatherShoulders | 83 | Gloamhide Mantle | T7 | 10 | Evasion 64-96 | Agility 3 |
| LeatherChest | 47 | Hide Jerkin | T9 | 1 | Evasion 32-48 | LocalIncreasedEvasion 5-8 |
| LeatherChest | 23 | Fernstalker Jerkin | T8 | 5 | Evasion 72-108 | LocalIncreasedEvasion 9-12 |
| LeatherChest | 91 | Gloamhide Jerkin | T7 | 10 | Evasion 128-192 | LocalIncreasedEvasion 13-16 |
| LeatherHands | 49 | Hide Grips | T9 | 1 | Evasion 13-19 | IncreasedProjectileDamage 2-3 |
| LeatherHands | 25 | Fernstalker Grips | T8 | 5 | Evasion 29-43 | IncreasedProjectileDamage 3-4 |
| LeatherHands | 93 | Gloamhide Grips | T7 | 10 | Evasion 51-77 | IncreasedProjectileDamage 5-6 |
| LeatherLegs | 48 | Hide Breeches | T9 | 1 | Evasion 26-38 | LightningResistance 1-2 |
| LeatherLegs | 24 | Fernstalker Breeches | T8 | 5 | Evasion 58-86 | LightningResistance 2-3 |
| LeatherLegs | 92 | Gloamhide Breeches | T7 | 10 | Evasion 102-154 | LightningResistance 3-4 |
| LeatherFeet | 50 | Hide Boots | T9 | 1 | Evasion 13-19 | MovementSpeed 2 |
| LeatherFeet | 26 | Fernstalker Boots | T8 | 5 | Evasion 29-43 | MovementSpeed 3 |
| LeatherFeet | 94 | Gloamhide Boots | T7 | 10 | Evasion 51-77 | MovementSpeed 4 |
| ClothHead | 41 | Linen Hood | T9 | 1 | Ward 6-8 | CooldownRecovery 1 |
| ClothHead | 17 | Mossweave Hood | T8 | 5 | Ward 13-19 | CooldownRecovery 2 |
| ClothHead | 95 | Duskweave Hood | T7 | 10 | Ward 22-32 | CooldownRecovery 3 |
| ClothShoulders | 78 | Linen Shoulderwraps | T9 | 1 | Ward 5-7 | Intellect 1 |
| ClothShoulders | 81 | Mossweave Shoulderwraps | T8 | 5 | Ward 10-16 | Intellect 2 |
| ClothShoulders | 84 | Duskweave Shoulderwraps | T7 | 10 | Ward 18-28 | Intellect 3 |
| ClothChest | 42 | Linen Robe | T9 | 1 | Ward 10-14 | IncreasedSpellDamage 2-3 |
| ClothChest | 18 | Mossweave Robe | T8 | 5 | Ward 21-31 | IncreasedSpellDamage 3-4 |
| ClothChest | 96 | Duskweave Robe | T7 | 10 | Ward 36-54 | IncreasedSpellDamage 5-6 |
| ClothHands | 44 | Linen Gloves | T9 | 1 | Ward 4-6 | CastSpeed 1 |
| ClothHands | 20 | Mossweave Gloves | T8 | 5 | Ward 8-12 | CastSpeed 2 |
| ClothHands | 98 | Duskweave Gloves | T7 | 10 | Ward 14-22 | CastSpeed 3 |
| ClothLegs | 43 | Linen Leggings | T9 | 1 | Ward 8-12 | Power 10-15 |
| ClothLegs | 19 | Mossweave Leggings | T8 | 5 | Ward 17-25 | Power 20-25 |
| ClothLegs | 97 | Duskweave Leggings | T7 | 10 | Ward 29-43 | Power 30-40 |
| ClothFeet | 45 | Linen Slippers | T9 | 1 | Ward 4-6 | ColdResistance 1-2 |
| ClothFeet | 21 | Mossweave Slippers | T8 | 5 | Ward 8-12 | ColdResistance 2-3 |
| ClothFeet | 99 | Duskweave Slippers | T7 | 10 | Ward 14-22 | ColdResistance 3-4 |
| Sword | 32 | Ironwood Sword | T9 | 1 | DamageMin 2-4, DamageMax 6-8, AttackTime 665-735 | CritMultiplier 2-3 |
| Sword | 7 | Bramblesteel Sword | T8 | 5 | DamageMin 5-7, DamageMax 8-12, AttackTime 665-735 | CritMultiplier 4-5 |
| Sword | 100 | Ironroot Sword | T7 | 10 | DamageMin 7-11, DamageMax 13-19, AttackTime 665-735 | CritMultiplier 5-6 |
| Mace | 35 | Oak Mace | T9 | 1 | DamageMin 3-5, DamageMax 7-11, AttackTime 855-945 | IncreasedPhysicalDamage 2-3 |
| Mace | 8 | Rootknot Mace | T8 | 5 | DamageMin 6-8, DamageMax 11-17, AttackTime 855-945 | IncreasedPhysicalDamage 3-4 |
| Mace | 101 | Ironroot Mace | T7 | 10 | DamageMin 10-14, DamageMax 17-25, AttackTime 855-945 | IncreasedPhysicalDamage 5-6 |
| Staff | 33 | Ash Staff | T9 | 1 | DamageMin 4-6, DamageMax 7-11, AttackTime 950-1050 | IncreasedSpellDamage 2-3 |
| Staff | 5 | Thornwood Staff | T8 | 5 | DamageMin 6-10, DamageMax 12-18, AttackTime 950-1050 | IncreasedSpellDamage 3-4 |
| Staff | 102 | Gloamwood Staff | T7 | 10 | DamageMin 10-16, DamageMax 18-26, AttackTime 950-1050 | IncreasedSpellDamage 5-6 |
| Bow | 34 | Hunter's Shortbow | T9 | 1 | DamageMin 3-5, DamageMax 6-8, AttackTime 760-840 | IncreasedProjectileDamage 2-3 |
| Bow | 6 | Briarstring Bow | T8 | 5 | DamageMin 6-8, DamageMax 9-13, AttackTime 760-840 | IncreasedProjectileDamage 3-4 |
| Bow | 103 | Duskstring Bow | T7 | 10 | DamageMin 9-13, DamageMax 14-20, AttackTime 760-840 | IncreasedProjectileDamage 5-6 |
| Shield | 70 | Oak Buckler | T9 | 1 | Armour 5-7, BlockChance 4-6 | Health 1-2 |
| Shield | 71 | Barkplate Shield | T8 | 5 | Armour 11-17, BlockChance 6-8 | Health 3-4 |
| Shield | 72 | Ironroot Kite Shield | T7 | 10 | Armour 19-29, BlockChance 8-12 | Health 5 |
| CasterOffHand | 73 | Ash Focus | T9 | 1 | Ward 6-10 | IncreasedSpellDamage 2-3 |
| CasterOffHand | 74 | Thornwood Focus | T8 | 5 | Ward 14-22 | IncreasedSpellDamage 3-4 |
| CasterOffHand | 75 | Gloamwood Orb | T7 | 10 | Ward 24-36 | IncreasedSpellDamage 5-6 |
| Amulet | 69 | Bone Charm | T9 | 1 | none | Strength 1 |
| Amulet | 130 | Tusk Charm | T8 | 5 | none | Strength 2 |
| Amulet | 131 | Horn Charm | T7 | 10 | none | Strength 3 |
| Amulet | 132 | Antler Charm | T9 | 1 | none | Agility 1 |
| Amulet | 133 | Hartsong Charm | T8 | 5 | none | Agility 2 |
| Amulet | 134 | Stagcrown Charm | T7 | 10 | none | Agility 3 |
| Amulet | 135 | Hollowfeather Charm | T9 | 1 | none | Intellect 1 |
| Amulet | 136 | Owlquill Charm | T8 | 5 | none | Intellect 2 |
| Amulet | 137 | Nightplume Charm | T7 | 10 | none | Intellect 3 |
| Amulet | 138 | Gloam Pendant | T9 | 1 | none | IncreasedDamageOverTime 1 |
| Amulet | 139 | Mirk Pendant | T8 | 5 | none | IncreasedDamageOverTime 2 |
| Amulet | 140 | Nightmire Pendant | T7 | 10 | none | IncreasedDamageOverTime 3 |
| Amulet | 141 | Mossheart Pendant | T9 | 1 | none | IncreasedHealing 2-3 |
| Amulet | 142 | Fernheart Pendant | T8 | 5 | none | IncreasedHealing 3-4 |
| Amulet | 143 | Elderheart Pendant | T7 | 10 | none | IncreasedHealing 5-6 |
| Amulet | 144 | Foxtooth Pendant | T9 | 1 | none | CritMultiplier 2-3 |
| Amulet | 145 | Wolfstooth Pendant | T8 | 5 | none | CritMultiplier 4-5 |
| Amulet | 146 | Bearclaw Pendant | T7 | 10 | none | CritMultiplier 5-6 |
| Amulet | 147 | Dewdrop Pendant | T9 | 1 | none | CooldownRecovery 1 |
| Amulet | 148 | Rainbead Pendant | T8 | 5 | none | CooldownRecovery 2 |
| Amulet | 149 | Wellspring Pendant | T7 | 10 | none | CooldownRecovery 3 |
| Ring | 68 | Copper Band | T9 | 1 | none | Health 1-2 |
| Ring | 104 | Oakbark Band | T8 | 5 | none | Health 3-4 |
| Ring | 105 | Elmroot Band | T7 | 10 | none | Health 5 |
| Ring | 106 | Thornwire Ring | T9 | 1 | none | AddedPhysicalDamageToAttacks 1 to 1 |
| Ring | 107 | Briarsteel Ring | T8 | 5 | none | AddedPhysicalDamageToAttacks 1 to 2 |
| Ring | 108 | Bramblefang Ring | T7 | 10 | none | AddedPhysicalDamageToAttacks 1 to 3 |
| Ring | 109 | Emberknot Ring | T9 | 1 | none | AddedFireDamageToSpells 1 to 1 |
| Ring | 110 | Cinderknot Ring | T8 | 5 | none | AddedFireDamageToSpells 1 to 2 |
| Ring | 111 | Ashcoil Ring | T7 | 10 | none | AddedFireDamageToSpells 1 to 3 |
| Ring | 112 | Hearthstone Ring | T9 | 1 | none | FireResistance 1-2 |
| Ring | 113 | Kilnstone Ring | T8 | 5 | none | FireResistance 2-3 |
| Ring | 114 | Forgeheart Ring | T7 | 10 | none | FireResistance 3-4 |
| Ring | 115 | Rimeglass Ring | T9 | 1 | none | ColdResistance 1-2 |
| Ring | 116 | Frostglass Ring | T8 | 5 | none | ColdResistance 2-3 |
| Ring | 117 | Winterglass Ring | T7 | 10 | none | ColdResistance 3-4 |
| Ring | 118 | Lodestone Ring | T9 | 1 | none | LightningResistance 1-2 |
| Ring | 119 | Stormlode Ring | T8 | 5 | none | LightningResistance 2-3 |
| Ring | 120 | Thunderlode Ring | T7 | 10 | none | LightningResistance 3-4 |
| Ring | 121 | Willowbark Ring | T9 | 1 | none | BlightResistance 1-2 |
| Ring | 122 | Fenwillow Ring | T8 | 5 | none | BlightResistance 2-3 |
| Ring | 123 | Elderwillow Ring | T7 | 10 | none | BlightResistance 3-4 |
| Ring | 124 | Sparkthorn Ring | T9 | 1 | none | AddedLightningDamageToAttacks 1 to 1 |
| Ring | 125 | Stormthorn Ring | T8 | 5 | none | AddedLightningDamageToAttacks 1 to 2 |
| Ring | 126 | Thunderthorn Ring | T7 | 10 | none | AddedLightningDamageToAttacks 1 to 3 |
| Ring | 127 | Frostbud Ring | T9 | 1 | none | AddedColdDamageToSpells 1 to 1 |
| Ring | 128 | Rimebud Ring | T8 | 5 | none | AddedColdDamageToSpells 1 to 2 |
| Ring | 129 | Winterbud Ring | T7 | 10 | none | AddedColdDamageToSpells 1 to 3 |
| Trinket | 150 | Withered Seed | - | 1 | none | IncreasedHealing 2-3 |
| Trinket | 151 | Carved Idol | - | 1 | none | CooldownRecovery 1 |

### Uniques

A unique's copy rolls its base's base stats and implicit, then its own lines.

| Template | Unique | Kind | Base | Lines |
| --- | --- | --- | --- | --- |
| 58 | Alpha's Fang Pendant | Amulet | 144 Foxtooth Pendant | Health 4-5, Strength 2-3, Agility 2-3, IncreasedAttackDamage 6-10 |
| 64 | Heartwood Band | Ring | 112 Hearthstone Ring | Health 4-5, BlightResistance 5-6, IncreasedHealing 5-7 |
| 65 | Thornheart Signet | Ring | 125 Stormthorn Ring | Health 4-5, AddedPhysicalDamageToAttacks 1 to 3, CritMultiplier 3-4 |
| 66 | Bramble Mother's Seed | Trinket | 150 Withered Seed | IncreasedMaximumLife 4-5, IncreasedDamageOverTime 2-3 |
| 67 | Tuskroot Idol | Trinket | 151 Carved Idol | IncreasedMaximumLife 4-5, IncreasedAttackDamage 5-7, IncreasedMeleeDamage 5-7 |
<!-- item-modifiers:end -->
