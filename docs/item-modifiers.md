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
- `ItemModifiers`: one row per tier of a group (`Tier` 1-5, `RequiredItemLevel`), or a unique's or implicit group's
  single row with no tier.
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
anything else. The items area (`ItemsPatch.Build`) also holds each template's links against its rarity: a unique
template links exactly its one unique modifier, a common base only implicits, and a template that breaks this keeps
none of its links.

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
item script's add, a quest's reward, a pickup of a drop that did not roll) and `CharacterCreateHandler` (a starting
item). A drop rolls its copy, implicits first, when it drops ([drops](#drops)), and its pickup adds that copy. Either way
the rows enter the character's `ItemModifierStore` marked for the save that inserts the copy, or are inserted with a new
character. The
copies owned before implicits existed got theirs at the drafted value (Character migration `AddImplicitsToOwnedItems`,
[inventory and saves](inventory-and-saves.md)), so none lost defence.

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
  else 1.
- **Base tier:** a base becomes the best tier of its line the item level reaches (item level 4 a tier-1 base, 5 the
  tier-2), and rolls that tier's implicits.
- **Rarity:** drawn by the creature's rarity (`Game:Items:Rarity`; a chest or a barrel by the Normal odds). A unique
  rolls its own modifier.
- **The boss's rare:** a Boss kill none of whose drops rolled Rare has its first drop that rolled a rarity rolled again,
  whole, as Rare. A unique is never touched.

The copy lies on the ground whole, its id, rarity, item level, name and modifiers included, and whoever picks it up gets
exactly that copy. A quest drop and any item that is not a base or a unique do not roll.

## Weights

Every prefix and suffix row weighs **1000, 800, 600, 400 and 200** for tiers 1 to 5 on each base kind its family rolls
on, and has no weight anywhere else. The families' "Rolls on" column below is that table: "armour" is all eighteen armour
kinds, "plate", "leather" and "cloth" an armour type's six, and "helmets", "shoulders", "gloves" and "boots" that slot in
all three types. Trinkets roll nothing.

## Local and global

A **local** modifier applies to the item it sits on: added damage, increased physical damage and attack speed on a
weapon change that weapon's hits only, and an armour piece's flat or increased defence changes that piece's defence only.
Every other modifier is **global** and applies to the character, among them the added damage "to attacks" (rings,
amulets, gloves) and "to spells" (rings, amulets, caster off-hands).

## Tiers

Tiers unlock at item level **1, 4, 7, 10 and 13**; the level cap is 15. S7 (global attack speed) and S13 (block) have
four tiers. An "adds X to Y" line rolls twice: the copy rolls its low end and its high end within the tier, and every hit
then rolls between them. The tier's drafted range X-Y is seeded as the low end's range X to m and the high end's m to Y,
with m the floor of their mean.

## Naming

- A magic item is named `<prefix name> <base name> <suffix name>`, a missing part dropped. The name is computed whenever
  the item is described, so renaming a family or a base renames every copy.
- A rare item gets a two-word name when it rolls, kept on the copy.
- Common and unique items show the template's name.
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
| P8 | Fire | Ashen | Prefix | IncreasedFireDamage | Staff, CasterOffHand, Amulet, Ring |
| P8 | Cold | Hoarfrost | Prefix | IncreasedColdDamage | Staff, CasterOffHand, Amulet, Ring |
| P8 | Lightning | Galvanic | Prefix | IncreasedLightningDamage | Staff, CasterOffHand, Amulet, Ring |
| P8 | Blight | Mouldering | Prefix | IncreasedBlightDamage | Staff, CasterOffHand, Amulet, Ring |
| P9 | Physical | Thorn-tipped | Prefix | AddedPhysicalDamageToAttacks | gloves, Amulet, Ring |
| P9 | Fire | Thorn-tipped | Prefix | AddedFireDamageToAttacks | gloves, Amulet, Ring |
| P9 | Cold | Thorn-tipped | Prefix | AddedColdDamageToAttacks | gloves, Amulet, Ring |
| P9 | Lightning | Thorn-tipped | Prefix | AddedLightningDamageToAttacks | gloves, Amulet, Ring |
| P9 | Blight | Thorn-tipped | Prefix | AddedBlightDamageToAttacks | gloves, Amulet, Ring |
| P10 | Fire | Ember-sung | Prefix | AddedFireDamageToSpells | CasterOffHand, Amulet, Ring |
| P10 | Cold | Ember-sung | Prefix | AddedColdDamageToSpells | CasterOffHand, Amulet, Ring |
| P10 | Lightning | Ember-sung | Prefix | AddedLightningDamageToSpells | CasterOffHand, Amulet, Ring |
| P11 |  | Hale | Prefix | Health | armour, Shield, Amulet, Ring |
| P12 |  | Barkbound | Prefix | Armor | plate, Shield |
| P13 |  | Ironbark | Prefix | LocalIncreasedArmor | plate, Shield |
| P14 |  | Fleet-footed | Prefix | Evasion | leather |
| P15 |  | Mistwoven | Prefix | LocalIncreasedEvasion | leather |
| P16 |  | Veilwoven | Prefix | Ward | cloth, CasterOffHand |
| P17 |  | Glamoured | Prefix | LocalIncreasedWard | cloth, CasterOffHand |
| P18 |  | Lingering | Prefix | IncreasedDamageOverTime | Staff, CasterOffHand, Amulet |
| P19 |  | Mending | Prefix | IncreasedHealing | Mace, Staff, CasterOffHand, Amulet |
| S1 |  | of the Ox-root | Suffix | Strength | armour, Sword, Mace, Bow, Shield, Amulet, Ring |
| S2 |  | of the Hare | Suffix | Agility | armour, Sword, Staff, Bow, Amulet, Ring |
| S3 |  | of the Owl | Suffix | Intellect | armour, Sword, Mace, Staff, CasterOffHand, Amulet, Ring |
| S4 |  | of the Old Oak | Suffix | Stamina | armour, Shield, Amulet, Ring |
| S5 | Fire | of the Hearth | Suffix | FireResistance | armour, Shield, Amulet, Ring |
| S5 | Cold | of the Thaw | Suffix | ColdResistance | armour, Shield, Amulet, Ring |
| S5 | Lightning | of Grounding | Suffix | LightningResistance | armour, Shield, Amulet, Ring |
| S5 | Blight | of the Clean Spring | Suffix | BlightResistance | armour, Shield, Amulet, Ring |
| S6 |  | of Haste | Suffix | LocalAttackSpeed | Sword, Mace, Staff, Bow |
| S7 |  | of Quick Hands | Suffix | AttackSpeed | gloves, Ring |
| S8 |  | of Cadence | Suffix | CastSpeed | Mace, Staff, CasterOffHand, Amulet, Ring |
| S9 |  | of the Keen Eye | Suffix | CritPct | Sword, Mace, Staff, Bow, CasterOffHand, Amulet, Ring |
| S10 |  | of Ruin | Suffix | CritMultiplier | Sword, Mace, Staff, Bow, Amulet |
| S11 |  | of the Wanderer | Suffix | MovementSpeed | boots |
| S12 |  | of Renewal | Suffix | CooldownRecovery | helmets, shoulders, Amulet |
| S13 |  | of the Bulwark | Suffix | BlockPct | Shield |

### Tiers

Each cell is the tier's range, then its weight on every base kind the family rolls on. An "adds" line reads as the range of its low end, then "to" and the range of its high end. A dash marks a tier the family does not have.

| Code | Element | T1 | T2 | T3 | T4 | T5 |
| --- | --- | --- | --- | --- | --- | --- |
| Item level | | 1 | 4 | 7 | 10 | 13 |
| P1 |  | 1 to 1-2 · 1000 | 2-3 to 3-4 · 800 | 3-4 to 4-6 · 600 | 5-6 to 6-8 · 400 | 6-8 to 8-11 · 200 |
| P2 |  | 1-2 to 2-3 · 1000 | 2-3 to 3-5 · 800 | 3-5 to 5-7 · 600 | 4-7 to 7-10 · 400 | 6-9 to 9-13 · 200 |
| P3 |  | 1 to 1-2 · 1000 | 2-3 to 3-4 · 800 | 3-4 to 4-6 · 600 | 4-6 to 6-8 · 400 | 5-8 to 8-11 · 200 |
| P4 |  | 1-2 to 2-4 · 1000 | 1-4 to 4-7 · 800 | 1-5 to 5-10 · 600 | 2-8 to 8-14 · 400 | 2-10 to 10-18 · 200 |
| P5 |  | 1 to 1-2 · 1000 | 2-3 to 3-4 · 800 | 3-4 to 4-6 · 600 | 4-6 to 6-8 · 400 | 5-8 to 8-11 · 200 |
| P6 |  | 10-19 · 1000 | 20-34 · 800 | 35-49 · 600 | 50-64 · 400 | 65-79 · 200 |
| P7 |  | 8-12 · 1000 | 13-20 · 800 | 21-30 · 600 | 31-40 · 400 | 41-50 · 200 |
| P8 | Fire | 5-9 · 1000 | 10-14 · 800 | 15-19 · 600 | 20-24 · 400 | 25-29 · 200 |
| P8 | Cold | 5-9 · 1000 | 10-14 · 800 | 15-19 · 600 | 20-24 · 400 | 25-29 · 200 |
| P8 | Lightning | 5-9 · 1000 | 10-14 · 800 | 15-19 · 600 | 20-24 · 400 | 25-29 · 200 |
| P8 | Blight | 5-9 · 1000 | 10-14 · 800 | 15-19 · 600 | 20-24 · 400 | 25-29 · 200 |
| P9 | Physical | 1 to 1-2 · 1000 | 1-2 to 2-3 · 800 | 2-3 to 3-4 · 600 | 2-3 to 3-5 · 400 | 3-4 to 4-6 · 200 |
| P9 | Fire | 1 to 1-2 · 1000 | 1-2 to 2-3 · 800 | 2-3 to 3-4 · 600 | 2-3 to 3-5 · 400 | 3-4 to 4-6 · 200 |
| P9 | Cold | 1 to 1-2 · 1000 | 1-2 to 2-3 · 800 | 2-3 to 3-4 · 600 | 2-3 to 3-5 · 400 | 3-4 to 4-6 · 200 |
| P9 | Lightning | 1 to 1-2 · 1000 | 1-2 to 2-3 · 800 | 2-3 to 3-4 · 600 | 2-3 to 3-5 · 400 | 3-4 to 4-6 · 200 |
| P9 | Blight | 1 to 1-2 · 1000 | 1-2 to 2-3 · 800 | 2-3 to 3-4 · 600 | 2-3 to 3-5 · 400 | 3-4 to 4-6 · 200 |
| P10 | Fire | 1 to 1-2 · 1000 | 1-2 to 2-3 · 800 | 2-3 to 3-4 · 600 | 2-3 to 3-5 · 400 | 3-4 to 4-6 · 200 |
| P10 | Cold | 1 to 1-2 · 1000 | 1-2 to 2-3 · 800 | 2-3 to 3-4 · 600 | 2-3 to 3-5 · 400 | 3-4 to 4-6 · 200 |
| P10 | Lightning | 1 to 1-2 · 1000 | 1-2 to 2-3 · 800 | 2-3 to 3-4 · 600 | 2-3 to 3-5 · 400 | 3-4 to 4-6 · 200 |
| P11 |  | 5-9 · 1000 | 10-19 · 800 | 20-29 · 600 | 30-44 · 400 | 45-59 · 200 |
| P12 |  | 2-4 · 1000 | 4-8 · 800 | 8-14 · 600 | 14-22 · 400 | 22-32 · 200 |
| P13 |  | 10-19 · 1000 | 20-34 · 800 | 35-49 · 600 | 50-64 · 400 | 65-79 · 200 |
| P14 |  | 10-19 · 1000 | 20-39 · 800 | 40-64 · 600 | 65-94 · 400 | 95-129 · 200 |
| P15 |  | 10-19 · 1000 | 20-34 · 800 | 35-49 · 600 | 50-64 · 400 | 65-79 · 200 |
| P16 |  | 3-5 · 1000 | 6-10 · 800 | 11-17 · 600 | 18-26 · 400 | 27-37 · 200 |
| P17 |  | 10-19 · 1000 | 20-34 · 800 | 35-49 · 600 | 50-64 · 400 | 65-79 · 200 |
| P18 |  | 6-10 · 1000 | 11-16 · 800 | 17-22 · 600 | 23-28 · 400 | 29-34 · 200 |
| P19 |  | 6-10 · 1000 | 11-16 · 800 | 17-22 · 600 | 23-28 · 400 | 29-34 · 200 |
| S1 |  | 1-3 · 1000 | 4-6 · 800 | 7-10 · 600 | 11-15 · 400 | 16-20 · 200 |
| S2 |  | 1-3 · 1000 | 4-6 · 800 | 7-10 · 600 | 11-15 · 400 | 16-20 · 200 |
| S3 |  | 1-3 · 1000 | 4-6 · 800 | 7-10 · 600 | 11-15 · 400 | 16-20 · 200 |
| S4 |  | 1-3 · 1000 | 4-6 · 800 | 7-10 · 600 | 11-15 · 400 | 16-20 · 200 |
| S5 | Fire | 6-11 · 1000 | 12-17 · 800 | 18-23 · 600 | 24-29 · 400 | 30-35 · 200 |
| S5 | Cold | 6-11 · 1000 | 12-17 · 800 | 18-23 · 600 | 24-29 · 400 | 30-35 · 200 |
| S5 | Lightning | 6-11 · 1000 | 12-17 · 800 | 18-23 · 600 | 24-29 · 400 | 30-35 · 200 |
| S5 | Blight | 6-11 · 1000 | 12-17 · 800 | 18-23 · 600 | 24-29 · 400 | 30-35 · 200 |
| S6 |  | 5-7 · 1000 | 8-10 · 800 | 11-13 · 600 | 14-16 · 400 | 17-19 · 200 |
| S7 |  | 3-4 · 1000 | 5-6 · 800 | 7-8 · 600 | 9-10 · 400 | - |
| S8 |  | 4-6 · 1000 | 7-9 · 800 | 10-12 · 600 | 13-15 · 400 | 16-18 · 200 |
| S9 |  | 1 · 1000 | 2 · 800 | 3 · 600 | 4 · 400 | 5 · 200 |
| S10 |  | 8-12 · 1000 | 13-17 · 800 | 18-22 · 600 | 23-27 · 400 | 28-32 · 200 |
| S11 |  | 5 · 1000 | 8 · 800 | 11 · 600 | 14 · 400 | 17 · 200 |
| S12 |  | 3-4 · 1000 | 5-6 · 800 | 7-8 · 600 | 9-10 · 400 | 11-12 · 200 |
| S13 |  | 3-4 · 1000 | 5-6 · 800 | 7-8 · 600 | 9-10 · 400 | - |

### Implicits

| Template | Base | Kind | Lines |
| --- | --- | --- | --- |
| 12 | Barkplate Helm | PlateHead | Armor 9-13 |
| 13 | Barkplate Chestguard | PlateChest | Armor 15-21 |
| 14 | Barkplate Legguards | PlateLegs | Armor 12-16 |
| 15 | Barkplate Gauntlets | PlateHands | Armor 6-8 |
| 16 | Barkplate Boots | PlateFeet | Armor 6-8 |
| 17 | Mossweave Hood | ClothHead | Ward 14-18 |
| 18 | Mossweave Robe | ClothChest | Ward 22-30 |
| 19 | Mossweave Leggings | ClothLegs | Ward 18-24 |
| 20 | Mossweave Gloves | ClothHands | Ward 9-12 |
| 21 | Mossweave Slippers | ClothFeet | Ward 9-12 |
| 22 | Fernstalker Cap | LeatherHead | Evasion 46-62 |
| 23 | Fernstalker Jerkin | LeatherChest | Evasion 77-104 |
| 24 | Fernstalker Breeches | LeatherLegs | Evasion 61-83 |
| 25 | Fernstalker Grips | LeatherHands | Evasion 31-41 |
| 26 | Fernstalker Boots | LeatherFeet | Evasion 31-41 |
| 36 | Ironbound Helm | PlateHead | Armor 4-6 |
| 37 | Ironbound Chestguard | PlateChest | Armor 7-9 |
| 38 | Ironbound Legguards | PlateLegs | Armor 5-7 |
| 39 | Ironbound Gauntlets | PlateHands | Armor 3 |
| 40 | Ironbound Boots | PlateFeet | Armor 3 |
| 41 | Linen Hood | ClothHead | Ward 6-8 |
| 42 | Linen Robe | ClothChest | Ward 10-14 |
| 43 | Linen Leggings | ClothLegs | Ward 9-12 |
| 44 | Linen Gloves | ClothHands | Ward 4-6 |
| 45 | Linen Slippers | ClothFeet | Ward 4-6 |
| 46 | Hide Cap | LeatherHead | Evasion 20-28 |
| 47 | Hide Jerkin | LeatherChest | Evasion 34-46 |
| 48 | Hide Breeches | LeatherLegs | Evasion 27-37 |
| 49 | Hide Grips | LeatherHands | Evasion 14-18 |
| 50 | Hide Boots | LeatherFeet | Evasion 14-18 |
| 70 | Oak Buckler | Shield | Armor 5-7, BlockPct 4-6 |
| 71 | Barkplate Shield | Shield | Armor 12-16, BlockPct 6-8 |
| 72 | Ironroot Kite Shield | Shield | Armor 20-28, BlockPct 9-12 |
| 73 | Ash Focus | CasterOffHand | Ward 7-9, IncreasedSpellDamage 4-6 |
| 74 | Thornwood Focus | CasterOffHand | Ward 15-21, IncreasedSpellDamage 7-9 |
| 75 | Gloamwood Orb | CasterOffHand | Ward 26-35, IncreasedSpellDamage 10-14 |
| 76 | Ironbound Pauldrons | PlateShoulders | Armor 3-5 |
| 77 | Hide Mantle | LeatherShoulders | Evasion 17-23 |
| 78 | Linen Shoulderwraps | ClothShoulders | Ward 5-7 |
| 79 | Barkplate Pauldrons | PlateShoulders | Armor 8-10 |
| 80 | Fernstalker Mantle | LeatherShoulders | Evasion 38-52 |
| 81 | Mossweave Shoulderwraps | ClothShoulders | Ward 11-15 |
| 82 | Ironroot Pauldrons | PlateShoulders | Armor 14-18 |
| 83 | Gloamhide Mantle | LeatherShoulders | Evasion 68-92 |
| 84 | Duskweave Shoulderwraps | ClothShoulders | Ward 20-26 |
| 85 | Ironroot Helm | PlateHead | Armor 16-22 |
| 86 | Ironroot Chestguard | PlateChest | Armor 27-37 |
| 87 | Ironroot Legguards | PlateLegs | Armor 22-30 |
| 88 | Ironroot Gauntlets | PlateHands | Armor 11-15 |
| 89 | Ironroot Boots | PlateFeet | Armor 11-15 |
| 90 | Gloamhide Cap | LeatherHead | Evasion 82-110 |
| 91 | Gloamhide Jerkin | LeatherChest | Evasion 136-184 |
| 92 | Gloamhide Breeches | LeatherLegs | Evasion 109-147 |
| 93 | Gloamhide Grips | LeatherHands | Evasion 54-74 |
| 94 | Gloamhide Boots | LeatherFeet | Evasion 54-74 |
| 95 | Duskweave Hood | ClothHead | Ward 23-31 |
| 96 | Duskweave Robe | ClothChest | Ward 38-52 |
| 97 | Duskweave Leggings | ClothLegs | Ward 31-41 |
| 98 | Duskweave Gloves | ClothHands | Ward 15-21 |
| 99 | Duskweave Slippers | ClothFeet | Ward 15-21 |

### Uniques

| Template | Unique | Kind | Lines |
| --- | --- | --- | --- |
| 58 | Alpha's Fang Pendant | Amulet | Health 10-14, Strength 2-4, Agility 2-4, IncreasedAttackDamage 6-10 |
| 64 | Heartwood Band | Ring | Health 16-24, BlightResistance 10-14, IncreasedHealing 6-10 |
| 65 | Thornheart Signet | Ring | Health 24-36, AddedPhysicalDamageToAttacks 2 to 3-5, CritPct 3-5 |
| 66 | Bramble Mother's Seed | Trinket | Health 20-30, IncreasedDamageOverTime 12-18, BlightResistance 8-12 |
| 67 | Tuskroot Idol | Trinket | Health 16-24, AttackSpeed 5-7, CastSpeed 5-7 |
<!-- item-modifiers:end -->
