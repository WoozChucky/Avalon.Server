# Item modifiers

An item template is a **base** (#1179): a kind of item with its own damage or defence, requirements and price. Each copy
of a base carries its own **modifiers**, rolled when the copy is made and kept on the copy as rows of the Character
database (`ItemInstanceModifiers`, [inventory and saves](inventory-and-saves.md)). This page is the modifier catalog:
what can roll, where, at which item level and how likely. Its tables are generated from the seed and checked against it
by `ItemModifierDocShould`; the prose around them is hand-written.

## The catalog

Five World database tables, all balance tables (seeded with `HasData`, changed only by a migration, never live-edited;
`/reload items` reads them again with the templates):

- `ItemModifierGroups`: one row per **exclusive group**. An item never carries two modifiers of one group. A group is an
  affix family (`Code` such as `P8` or `S13`, and `Element` for a family split by element), a unique's own modifier, or
  one base's implicits. Kind and name live here only.
- `ItemModifiers`: one row per tier of a group (`Tier` 1-9, counting down, and `RequiredItemLevel`), or a unique's or
  implicit group's single row with no tier.
- `ItemModifierStats`: a row's stat lines, each a range (`Min`-`Max`), and for an "adds X to Y" line a second range
  (`Min2`-`Max2`) for its high end. A row may have several lines.
- `ItemModifierWeights`: how likely a prefix or suffix row is on each base kind. No row, or weight 0, means it never
  rolls there.
- `ItemTemplateModifiers`: the modifiers every copy of a template rolls when it is made, in order: a base's implicits, or
  a unique's own modifier.

Ids are surrogate numbers that mean nothing. Each group and each row has its id written as a literal in the seed table
(`ItemModifierSeed`); none is computed from where a row sits. An item names a catalog row by `ModifierId` (`integer`), so
an id never changes once seeded: a row added later takes the next free id, and `SeedIntegrityShould` holds every row
to the id committed in `tests/Avalon.Server.World.UnitTests/Seeding/item-modifier-ids.tsv` (a new row adds a line).
Nothing computes meaning from an id; a tier, a group or a kind is always read from its column.

`ItemModifierCatalog` loads the catalog for the world server and the REST API. It logs and leaves out a row with no stat
line, a prefix or suffix row without a tier or a unique or implicit row with one, a unique or implicit row that carries
a weight, and a template's links when they name a missing row, a prefix or a suffix, or mix a unique modifier with
anything else. Each template's links are also held against its rarity (`ItemModifierCatalog.RarityRefusal`): a unique
template links exactly its one unique modifier, a common base only implicits. The world's items area (`ItemsPatch.Build`)
logs and leaves out a template whose links break this, whole, so no copy of it is ever made (a stat-less unique, or a
unique standing in its line as a base); a unique template whose one modifier the catalog left out is kept, and its
drops are removed when they roll.

The REST API never reads the whole catalog. A tooltip or a link preview reads one template's links and the rows they
name, with their groups and stat lines and no weights (`IItemModifierRepository.GetLinkedToAsync`), held to the same
rules on that subset (but the weight one, which needs the weights; `SeedIntegrityShould` holds that no unique or
implicit carries one), and the template's links dropped when they break its rarity. A character's inventory reads only
the rows its items carry (`GetByIdsAsync`), for their kinds and their names. A live template edit that changes an
item's rarity is refused, keyed `rarity`, when the template's links would break it at the new rarity
([live template editing](live-template-editing.md)).

## Rarity

- **Common**: the base and its implicits, nothing else.
- **Magic**: one or two affixes, at most one prefix and one suffix.
- **Rare**: three to six affixes, at most three prefixes and three suffixes.
- **Unique**: hand-made; its stats are its one unique modifier, every line a range each copy rolls in.

## Implicits

Every base that gives a defence has one implicit modifier: an armour piece's armour, evasion or ward, a shield's armour
and block, a caster off-hand's ward and increased spell damage. Each line is the base's drafted value ±15 %, rounded half
away from zero and at least 1, and every copy rolls it, however the copy is made (a drop, a vendor, a quest, a starting
item). Implicits count toward neither affix limit and never block an affix: an armour implicit and the flat armour prefix
sit on one piece. Weapons keep their damage and attack time on the base; rings, amulets and trinkets have no implicit.

A base's template carries no defence line of its own (World migration `MoveBaseStatsToImplicits`): the stats fold reads
the copy's implicit values as that item's own defence, scaled by its local increases like any other armour, evasion or
ward line. A copy made outright goes through `IItemRoller.RollPlain`: `IInventoryService.TryAdd` (a vendor purchase, an
item script's add, a quest reward that is not a base, a pickup of a drop that did not roll) and
`CharacterCreateHandler` (a starting item). A drop rolls its copy, implicits first, when it drops ([drops](#drops)),
and its pickup adds that copy. Either way the rows enter the character's `ItemModifierStore` marked for the save that
inserts the copy, or are inserted with a new character. The copies owned before implicits existed got theirs at the
drafted value (Character migration `AddImplicitsToOwnedItems`, [inventory and saves](inventory-and-saves.md)), so none
lost defence.

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
- **Base tier:** a base becomes the best tier of its line the item level reaches (item level 4 a T9 base, 5 the
  T8), and rolls that tier's implicits.
- **Rarity:** drawn by the creature's rarity (`Game:Items:Rarity`; a chest or a barrel by the Normal odds). A unique
  rolls its own modifier.
- **The boss's rare:** a Boss kill none of whose drops rolled Rare has its first drop that rolled a rarity rolled again,
  whole, as Rare. A unique is never touched.

The copy lies on the ground whole, its id, rarity, item level, name and modifiers included, and whoever picks it up gets
exactly that copy. A quest drop and any item that is not a base or a unique do not roll.

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
  quest's level requirement (at least 1), its tier the best its line reaches there, which the turn-in's Unique and room
  checks judge too, and the offer shows it as Magic with the tier it is paid as (`QuestRewardItemDto.Rarity` and
  `PaidItemTemplateId`, [quests](quests.md)). Any other reward is a plain copy.
- **A vendor** sells only Common items (`VendorCatalog` refuses any other row, and a row whose item a `/reload items`
  made Unique since is hidden and refused, `VendorRules.IsSoldNew`), each a plain copy with its implicits. A
  sale pays the template's `SellPrice` times the copy's rarity factor (`Game:Items:SellFactor`: Magic 2, Rare 4,
  Unique 6, Common 1; [vendors](inventory-and-saves.md)).

## Weights

Every prefix and suffix row weighs **1000, 800 and 600** for T9, T8 and T7 on each base kind its group rolls on, and
has no weight anywhere else. The families' "Rolls on" column below is that table: "armour" is all eighteen armour kinds,
"plate", "leather" and "cloth" an armour type's six, and "helmets", "shoulders", "chests", "gloves", "legs" and "boots"
that slot in all three types. Trinkets roll nothing.

Each family rolls on a few fitting slots only, so a slot has an identity and a roll stays readable, and so that one
full set of the best level-10 items stays within the starting zone's gear budget (each resistance at most 25 %, attack
and cast speed 10 %, movement speed 8 %, crit chance +6 %, life +75 flat and +26 % increased, each main attribute +15),
counting implicits and unique lines, which stack with an affix of the same stat. A split family may roll some of its
elements on a slot and not others: each resistance rolls on one armour slot of its own (fire on helmets, cold on boots,
lightning on legs, blight on chests) besides the shield, the caster off-hand and the amulet.

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
- `ItemNames.Display` (`Avalon.Domain`) gives the name, or null where the template's name is the item's, from a magic
  copy's lowest-ordinal prefix and suffix. It runs when a copy is first described in an items generation, never per
  tick: `ItemSlotDto` field 11
  (the login snapshot, inventory updates, item replies, the bank, the buyback), `TradeItemDto` field 8 and
  `LootDropDto` field 11 send it as an optional string, absent for a common or unique copy, and the REST inventory
  read's item `name` likewise. The world holds a magic copy's name as a field beside its modifiers
  (`DescribedName`, on the copy's entry in `ItemModifierStore` and on a drop on the ground), keyed by the items
  generation it was worked out in, so describing the same copy again builds no string, a reload's rename reaches it at
  its next describe, and a copy whose modifiers change is a new entry. A rare copy keeps its stored name even when a
  reload removed its template.
- A family has one name for all its tiers (the tier shows as a number), one per element on a split family. Implicits and
  unique modifiers never name an item.

## The tables

<!-- item-modifiers:begin -->
<!-- Generated by tools/Avalon.Exporter -- item-modifier-doc from ItemModifierSeed. Do not edit by hand. -->

### Families

| Code | Element | Name | Kind | Stat | Rolls on |
| --- | --- | --- | --- | --- | --- |
| P1 |  | Briar-edged | Prefix | AddedPhysicalDamage | Sword, Mace, Staff, Bow |
| P2 |  | Smouldering | Prefix | AddedFireDamage | Sword, Mace, Staff, Bow |
| P3 |  | Rimed | Prefix | AddedColdDamage | Sword, Mace, Staff, Bow |
| P4 |  | Crackling | Prefix | AddedLightningDamage | Sword, Mace, Staff, Bow |
| P5 |  | Festering | Prefix | AddedBlightDamage | Sword, Mace, Staff, Bow |
| P6 |  | Honed | Prefix | LocalIncreasedPhysicalDamage | Sword, Mace, Staff, Bow |
| P7 |  | Whispering | Prefix | IncreasedSpellDamage | Mace, Staff, CasterOffHand |
| P8 | Fire | Ashen | Prefix | IncreasedFireDamage | shoulders, Staff, CasterOffHand, Amulet, Ring |
| P8 | Cold | Hoarfrost | Prefix | IncreasedColdDamage | shoulders, Staff, CasterOffHand, Amulet, Ring |
| P8 | Lightning | Galvanic | Prefix | IncreasedLightningDamage | shoulders, Staff, CasterOffHand, Amulet, Ring |
| P8 | Blight | Mouldering | Prefix | IncreasedBlightDamage | shoulders, Staff, CasterOffHand, Amulet, Ring |
| P9 | Physical | Thorn-tipped | Prefix | AddedPhysicalDamageToAttacks | gloves |
| P9 | Fire | Thorn-tipped | Prefix | AddedFireDamageToAttacks | gloves |
| P9 | Cold | Thorn-tipped | Prefix | AddedColdDamageToAttacks | gloves |
| P9 | Lightning | Thorn-tipped | Prefix | AddedLightningDamageToAttacks | gloves |
| P9 | Blight | Thorn-tipped | Prefix | AddedBlightDamageToAttacks | gloves |
| P10 | Fire | Ember-sung | Prefix | AddedFireDamageToSpells | Ring |
| P10 | Cold | Ember-sung | Prefix | AddedColdDamageToSpells | Ring |
| P10 | Lightning | Ember-sung | Prefix | AddedLightningDamageToSpells | Ring |
| P11 |  | Hale | Prefix | Health | helmets, chests, legs, boots, Shield, Amulet |
| P12 |  | Barkbound | Prefix | Armor | plate, Shield |
| P13 |  | Ironbark | Prefix | LocalIncreasedArmor | plate, Shield |
| P14 |  | Fleet-footed | Prefix | Evasion | leather |
| P15 |  | Mistwoven | Prefix | LocalIncreasedEvasion | leather |
| P16 |  | Veilwoven | Prefix | Ward | cloth, CasterOffHand |
| P17 |  | Glamoured | Prefix | LocalIncreasedWard | cloth, CasterOffHand |
| P18 |  | Lingering | Prefix | IncreasedDamageOverTime | Staff, CasterOffHand, Amulet |
| P19 |  | Mending | Prefix | IncreasedHealing | Mace, Staff, CasterOffHand, Amulet |
| S1 |  | of the Ox-root | Suffix | Strength | chests, Ring |
| S2 |  | of the Hare | Suffix | Agility | legs, Ring |
| S3 |  | of the Owl | Suffix | Intellect | shoulders, Ring |
| S4 |  | of the Old Oak | Suffix | Stamina | boots |
| S5 | Fire | of the Hearth | Suffix | FireResistance | helmets, Shield, CasterOffHand, Amulet |
| S5 | Cold | of the Thaw | Suffix | ColdResistance | boots, Shield, CasterOffHand, Amulet |
| S5 | Lightning | of Grounding | Suffix | LightningResistance | legs, Shield, CasterOffHand, Amulet |
| S5 | Blight | of the Clean Spring | Suffix | BlightResistance | chests, Shield, CasterOffHand, Amulet |
| S6 |  | of Haste | Suffix | LocalAttackSpeed | Sword, Mace, Staff, Bow |
| S7 |  | of Quick Hands | Suffix | AttackSpeed | gloves |
| S8 |  | of Cadence | Suffix | CastSpeed | gloves, Staff, CasterOffHand |
| S9 |  | of the Keen Eye | Suffix | CritPct | gloves, Sword, Mace, Staff, Bow |
| S10 |  | of Ruin | Suffix | CritMultiplier | helmets, gloves, Sword, Mace, Staff, Bow, CasterOffHand |
| S11 |  | of the Wanderer | Suffix | MovementSpeed | boots |
| S12 |  | of Renewal | Suffix | CooldownRecovery | helmets, shoulders |
| S13 |  | of the Bulwark | Suffix | BlockPct | Shield |
| S14 |  | of Deep Roots | Suffix | IncreasedMaximumLife | shoulders, chests, legs |

### Tiers

Each cell is the tier's range, then its weight on every base kind the family rolls on. An "adds" line reads as its low end, then "to" and its high end. Tiers count down: T9 is the weakest. A dash marks a tier the family does not have.

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

| Template | Base | Kind | Lines |
| --- | --- | --- | --- |

### Uniques

| Template | Unique | Kind | Lines |
| --- | --- | --- | --- |
| 58 | Alpha's Fang Pendant | Amulet | Health 10-14, Strength 2-4, Agility 2-4, IncreasedAttackDamage 6-10 |
| 64 | Heartwood Band | Ring | Health 16-24, BlightResistance 10-14, IncreasedHealing 6-10 |
| 65 | Thornheart Signet | Ring | Health 24-36, AddedPhysicalDamageToAttacks 2 to 3-5, CritPct 3-5 |
| 66 | Bramble Mother's Seed | Trinket | Health 20-30, IncreasedDamageOverTime 12-18, BlightResistance 8-12 |
| 67 | Tuskroot Idol | Trinket | Health 16-24, AttackSpeed 5-7, CastSpeed 5-7 |
<!-- item-modifiers:end -->
