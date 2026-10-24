using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;

namespace Avalon.Database.World.Seeding;

/// <summary>A base stat as drafted (#1179): the value its seeded range (<see cref="ItemBaseSeed.BaseStats" />) spreads.</summary>
public readonly record struct DraftedBaseStat(ulong TemplateId, ItemBaseStat Stat, int Value);

/// <summary>A base's implicit (#1179): the stat of the implicit group its template links, at the base's own tier.</summary>
public readonly record struct BaseImplicit(ulong TemplateId, StatType Stat);

/// <summary>
/// The seeded equipment (#1179): 122 bases and 5 uniques. A base is Common, lists no classes and carries no stat line,
/// only its requirements, in three tiers at required levels 1, 5 and 10 (armour and weapons ask their attribute none, 30
/// and 45; jewellery none). Plate asks Strength, leather Agility, cloth Intellect. Rings (nine lines) and amulets (seven)
/// come in lines of three tiers, one line per implicit (ids 104-149); the two trinket bases (150, 151), one tier each,
/// exist only for the trinket uniques to name, and nothing hands one out. A base's defence (an armour piece's armour,
/// evasion or ward, a shield's armour and block, a caster off-hand's ward) and a weapon's damage and attack time are its
/// base stats, below, which every copy rolls; a weapon's template names the type of its damage (its damage and
/// attack-time columns are no longer read). Every base also carries one themed implicit (<see cref="Implicits" />),
/// matched to its tier. A unique names its base (<see cref="ItemTemplate.BaseTemplateId" />), whose base stats and
/// implicit every copy rolls before the unique's own modifier (ItemModifierSeed). Numbers are rounded half away from
/// zero and at least 1; every base sells for a quarter of its price. The model's HasData reads these; consumables and
/// quest items stay in WorldDbContext, with no base kind.
/// <para>
/// Every base authors a range for each base stat its kind carries (ItemBaseStatRules), which every copy rolls:
/// <see cref="Drafted" /> keeps each drafted value, and <see cref="BaseStats" /> spreads it by 20 %, or by 5 % for an
/// attack time. The drafted values: a chest's 8 / 18 / 32 armour (plate), 40 / 90 / 160 evasion (leather) and
/// 12 / 26 / 45 ward (cloth) by tier, the other slots a share of it (legs 80 %, head 60 %, shoulders 50 %, hands and
/// feet 40 %); a shield's 6 / 14 / 24 armour and 5 / 7 / 10 block; a caster off-hand's 8 / 18 / 30 ward; a weapon its
/// kind's damage by tier (sword 3-7 / 6-10 / 9-16, bow 4-7 / 7-11 / 11-17, mace 4-9 / 7-14 / 12-21, staff 5-9 / 8-15 /
/// 13-22) and its kind's pace (sword 700, bow 800, mace 900 and staff 1000 ms).
/// </para>
/// </summary>
public static class ItemBaseSeed
{
    private const int BaseStatSpreadPercent = 20;
    private const int AttackTimeSpreadPercent = 5;

    private const ushort Tier1Level = 1;
    private const ushort Tier2Level = 5;
    private const ushort Tier3Level = 10;
    private const ushort Tier2Attribute = 30;
    private const ushort Tier3Attribute = 45;

    private enum Family { Plate, Leather, Cloth }

    private enum ArmourSlot { Head, Chest, Legs, Hands, Feet, Shoulders }

    private enum Attribute { Strength, Agility, Intellect }

    public static ItemTemplate[] Equipment() =>
    [
        // Plate: Strength. Head, chest, legs, hands, feet, shoulders; tiers 1, 2 and 3.
        .. ArmourSet(Family.Plate, 1, ["Ironbound Helm", "Ironbound Chestguard", "Ironbound Legguards", "Ironbound Gauntlets", "Ironbound Boots", "Ironbound Pauldrons"], [36, 37, 38, 39, 40, 76]),
        .. ArmourSet(Family.Plate, 2, ["Barkplate Helm", "Barkplate Chestguard", "Barkplate Legguards", "Barkplate Gauntlets", "Barkplate Boots", "Barkplate Pauldrons"], [12, 13, 14, 15, 16, 79]),
        .. ArmourSet(Family.Plate, 3, ["Ironroot Helm", "Ironroot Chestguard", "Ironroot Legguards", "Ironroot Gauntlets", "Ironroot Boots", "Ironroot Pauldrons"], [85, 86, 87, 88, 89, 82]),

        // Leather: Agility.
        .. ArmourSet(Family.Leather, 1, ["Hide Cap", "Hide Jerkin", "Hide Breeches", "Hide Grips", "Hide Boots", "Hide Mantle"], [46, 47, 48, 49, 50, 77]),
        .. ArmourSet(Family.Leather, 2, ["Fernstalker Cap", "Fernstalker Jerkin", "Fernstalker Breeches", "Fernstalker Grips", "Fernstalker Boots", "Fernstalker Mantle"], [22, 23, 24, 25, 26, 80]),
        .. ArmourSet(Family.Leather, 3, ["Gloamhide Cap", "Gloamhide Jerkin", "Gloamhide Breeches", "Gloamhide Grips", "Gloamhide Boots", "Gloamhide Mantle"], [90, 91, 92, 93, 94, 83]),

        // Cloth: Intellect.
        .. ArmourSet(Family.Cloth, 1, ["Linen Hood", "Linen Robe", "Linen Leggings", "Linen Gloves", "Linen Slippers", "Linen Shoulderwraps"], [41, 42, 43, 44, 45, 78]),
        .. ArmourSet(Family.Cloth, 2, ["Mossweave Hood", "Mossweave Robe", "Mossweave Leggings", "Mossweave Gloves", "Mossweave Slippers", "Mossweave Shoulderwraps"], [17, 18, 19, 20, 21, 81]),
        .. ArmourSet(Family.Cloth, 3, ["Duskweave Hood", "Duskweave Robe", "Duskweave Leggings", "Duskweave Gloves", "Duskweave Slippers", "Duskweave Shoulderwraps"], [95, 96, 97, 98, 99, 84]),

        // Weapons: each type keeps its attack time; 4-7, 7-11 and 11-17 physical. The mace asks Strength or Intellect.
        Weapon(32, "Ironwood Sword", ItemBaseKind.Sword, ItemSubClass.OneHanded, 1, 792, Attribute.Strength),
        Weapon(7, "Bramblesteel Sword", ItemBaseKind.Sword, ItemSubClass.OneHanded, 2, 792, Attribute.Strength),
        Weapon(100, "Ironroot Sword", ItemBaseKind.Sword, ItemSubClass.OneHanded, 3, 792, Attribute.Strength),
        Weapon(35, "Oak Mace", ItemBaseKind.Mace, ItemSubClass.OneHanded, 1, 784, Attribute.Strength, Attribute.Intellect),
        Weapon(8, "Rootknot Mace", ItemBaseKind.Mace, ItemSubClass.OneHanded, 2, 784, Attribute.Strength, Attribute.Intellect),
        Weapon(101, "Ironroot Mace", ItemBaseKind.Mace, ItemSubClass.OneHanded, 3, 784, Attribute.Strength, Attribute.Intellect),
        Weapon(33, "Ash Staff", ItemBaseKind.Staff, ItemSubClass.TwoHanded, 1, 800, Attribute.Intellect),
        Weapon(5, "Thornwood Staff", ItemBaseKind.Staff, ItemSubClass.TwoHanded, 2, 800, Attribute.Intellect),
        Weapon(102, "Gloamwood Staff", ItemBaseKind.Staff, ItemSubClass.TwoHanded, 3, 800, Attribute.Intellect),
        Weapon(34, "Hunter's Shortbow", ItemBaseKind.Bow, ItemSubClass.Ranged, 1, 801, Attribute.Agility),
        Weapon(6, "Briarstring Bow", ItemBaseKind.Bow, ItemSubClass.Ranged, 2, 801, Attribute.Agility),
        Weapon(103, "Duskstring Bow", ItemBaseKind.Bow, ItemSubClass.Ranged, 3, 801, Attribute.Agility),

        // Off hands: a shield asks Strength, a caster off-hand Intellect.
        OffHand(70, "Oak Buckler", ItemBaseKind.Shield, ItemSubClass.Shield, 1, Attribute.Strength),
        OffHand(71, "Barkplate Shield", ItemBaseKind.Shield, ItemSubClass.Shield, 2, Attribute.Strength),
        OffHand(72, "Ironroot Kite Shield", ItemBaseKind.Shield, ItemSubClass.Shield, 3, Attribute.Strength),
        OffHand(73, "Ash Focus", ItemBaseKind.CasterOffHand, ItemSubClass.CasterOffHand, 1, Attribute.Intellect),
        OffHand(74, "Thornwood Focus", ItemBaseKind.CasterOffHand, ItemSubClass.CasterOffHand, 2, Attribute.Intellect),
        OffHand(75, "Gloamwood Orb", ItemBaseKind.CasterOffHand, ItemSubClass.CasterOffHand, 3, Attribute.Intellect),

        // Jewellery: rings and amulets in lines of three tiers, each line its own implicit (s_rings, s_amulets).
        .. s_rings.SelectMany(line => JewelleryLine(ItemBaseKind.Ring, line)),
        .. s_amulets.SelectMany(line => JewelleryLine(ItemBaseKind.Amulet, line)),

        // The trinket bases, which only uniques name: one tier each, never dropped, sold or given out.
        .. s_trinkets.Select(trinket => Base(trinket.Id, trinket.Name, ItemBaseKind.Trinket, ItemClass.Armor,
            ItemSubClass.Trinket, ItemSlotType.Trinket, tier: 1, buyPrice: 200)),

        // The uniques: hand-made, a required level, prices and the base each is made on (one of its own kind, at or
        // under its level), whose base stats and implicit every copy rolls. Their own stats are their unique modifiers,
        // rolled per copy (ItemModifierSeed).
        Unique(58, "Alpha's Fang Pendant", ItemBaseKind.Amulet, ItemSubClass.Amulet, ItemSlotType.Neck, 3, 50, 144),
        Unique(64, "Heartwood Band", ItemBaseKind.Ring, ItemSubClass.Ring, ItemSlotType.Finger, 4, 100, 112),
        Unique(65, "Thornheart Signet", ItemBaseKind.Ring, ItemSubClass.Ring, ItemSlotType.Finger, 5, 200, 125),
        Unique(66, "Bramble Mother's Seed", ItemBaseKind.Trinket, ItemSubClass.Trinket, ItemSlotType.Trinket, 5, 200, 150),
        Unique(67, "Tuskroot Idol", ItemBaseKind.Trinket, ItemSubClass.Trinket, ItemSlotType.Trinket, 4, 100, 151),
    ];

    /// <summary>
    /// Every base's implicit stat (#1179), by template id: the content table's themed line, one per base. The tier is the
    /// base's own (required level 1, 5 and 10 are T9, T8 and T7; a trinket base, at level 1, T9), and the modifier
    /// catalog links the row of that stat's implicit group at that tier (ItemModifierSeed.TemplateLinks).
    /// </summary>
    public static BaseImplicit[] Implicits() =>
    [
        .. Equipment()
            .Where(template => template.Rarity == ItemRarity.Common && template.BaseKind is not null)
            .OrderBy(template => template.Id.Value)
            .Select(template => new BaseImplicit(template.Id.Value, ImplicitOf(template.Id.Value, template.BaseKind!.Value))),
    ];

    /// <summary>The implicit tier of a base at <paramref name="level" />: 9, 8 or 7 at required level 1, 5 or 10.</summary>
    public static byte ImplicitTierOf(ushort? level) => (byte)(10 - TierOf(level));

    // The ring lines, T9 to T7: life, two attack lines, two spell lines and the four resistances (the content table's
    // order, which the new ids 104-129 follow; the Copper Band, 68, is the life line's first tier).
    private static readonly (StatType Stat, ulong[] Ids, string[] Names)[] s_rings =
    [
        (StatType.Health, [68, 104, 105], ["Copper Band", "Oakbark Band", "Elmroot Band"]),
        (StatType.AddedPhysicalDamageToAttacks, [106, 107, 108], ["Thornwire Ring", "Briarsteel Ring", "Bramblefang Ring"]),
        (StatType.AddedFireDamageToSpells, [109, 110, 111], ["Emberknot Ring", "Cinderknot Ring", "Ashcoil Ring"]),
        (StatType.FireResistance, [112, 113, 114], ["Hearthstone Ring", "Kilnstone Ring", "Forgeheart Ring"]),
        (StatType.ColdResistance, [115, 116, 117], ["Rimeglass Ring", "Frostglass Ring", "Winterglass Ring"]),
        (StatType.LightningResistance, [118, 119, 120], ["Lodestone Ring", "Stormlode Ring", "Thunderlode Ring"]),
        (StatType.BlightResistance, [121, 122, 123], ["Willowbark Ring", "Fenwillow Ring", "Elderwillow Ring"]),
        (StatType.AddedLightningDamageToAttacks, [124, 125, 126], ["Sparkthorn Ring", "Stormthorn Ring", "Thunderthorn Ring"]),
        (StatType.AddedColdDamageToSpells, [127, 128, 129], ["Frostbud Ring", "Rimebud Ring", "Winterbud Ring"]),
    ];

    // The amulet lines, T9 to T7: one per attribute, then damage over time, healing, crit multiplier and cooldown
    // recovery (new ids 130-149; the Bone Charm, 69, is the Strength line's first tier).
    private static readonly (StatType Stat, ulong[] Ids, string[] Names)[] s_amulets =
    [
        (StatType.Strength, [69, 130, 131], ["Bone Charm", "Tusk Charm", "Horn Charm"]),
        (StatType.Agility, [132, 133, 134], ["Antler Charm", "Hartsong Charm", "Stagcrown Charm"]),
        (StatType.Intellect, [135, 136, 137], ["Hollowfeather Charm", "Owlquill Charm", "Nightplume Charm"]),
        (StatType.IncreasedDamageOverTime, [138, 139, 140], ["Gloam Pendant", "Mirk Pendant", "Nightmire Pendant"]),
        (StatType.IncreasedHealing, [141, 142, 143], ["Mossheart Pendant", "Fernheart Pendant", "Elderheart Pendant"]),
        (StatType.CritMultiplier, [144, 145, 146], ["Foxtooth Pendant", "Wolfstooth Pendant", "Bearclaw Pendant"]),
        (StatType.CooldownRecovery, [147, 148, 149], ["Dewdrop Pendant", "Rainbead Pendant", "Wellspring Pendant"]),
    ];

    // The two trinket bases, at required level 1: the bases the trinket uniques are made on.
    private static readonly (ulong Id, string Name, StatType Stat)[] s_trinkets =
    [
        (150, "Withered Seed", StatType.IncreasedHealing),
        (151, "Carved Idol", StatType.CooldownRecovery),
    ];

    // A base's implicit stat: an armour piece's by its family and slot, a weapon's and an off-hand's by kind, a ring's or
    // an amulet's by its line, a trinket base's its own.
    private static StatType ImplicitOf(ulong id, ItemBaseKind kind) => kind switch
    {
        ItemBaseKind.PlateHead => StatType.FireResistance,
        ItemBaseKind.PlateShoulders => StatType.LocalIncreasedArmor,
        ItemBaseKind.PlateChest => StatType.IncreasedMaximumLife,
        ItemBaseKind.PlateHands => StatType.AddedPhysicalDamageToAttacks,
        ItemBaseKind.PlateLegs => StatType.Strength,
        ItemBaseKind.PlateFeet => StatType.Stamina,
        ItemBaseKind.LeatherHead => StatType.CritMultiplier,
        ItemBaseKind.LeatherShoulders => StatType.Agility,
        ItemBaseKind.LeatherChest => StatType.LocalIncreasedEvasion,
        ItemBaseKind.LeatherHands => StatType.IncreasedProjectileDamage,
        ItemBaseKind.LeatherLegs => StatType.LightningResistance,
        ItemBaseKind.LeatherFeet => StatType.MovementSpeed,
        ItemBaseKind.ClothHead => StatType.CooldownRecovery,
        ItemBaseKind.ClothShoulders => StatType.Intellect,
        ItemBaseKind.ClothChest => StatType.IncreasedSpellDamage,
        ItemBaseKind.ClothHands => StatType.CastSpeed,
        ItemBaseKind.ClothLegs => StatType.Power,
        ItemBaseKind.ClothFeet => StatType.ColdResistance,
        ItemBaseKind.Sword => StatType.CritMultiplier,
        ItemBaseKind.Mace => StatType.IncreasedPhysicalDamage,
        ItemBaseKind.Staff => StatType.IncreasedSpellDamage,
        ItemBaseKind.Bow => StatType.IncreasedProjectileDamage,
        ItemBaseKind.Shield => StatType.Health,
        ItemBaseKind.CasterOffHand => StatType.IncreasedSpellDamage,
        ItemBaseKind.Ring => s_rings.Single(line => line.Ids.Contains(id)).Stat,
        ItemBaseKind.Amulet => s_amulets.Single(line => line.Ids.Contains(id)).Stat,
        _ => s_trinkets.Single(trinket => trinket.Id == id).Stat,
    };

    /// <summary>Every base's drafted base stats, by template id and then stat.</summary>
    public static DraftedBaseStat[] Drafted() =>
    [
        .. Equipment()
            .Where(template => template.Rarity == ItemRarity.Common && template.BaseKind is not null)
            .OrderBy(template => template.Id.Value)
            .SelectMany(template => DraftedOf(template.Id.Value, template.BaseKind!.Value, TierOf(template.RequiredLevel))),
    ];

    /// <summary>
    /// Every base's base-stat ranges (HasData): each drafted value spread by 20 %, an attack time by 5 %, each end rounded
    /// half away from zero and at least 1.
    /// </summary>
    public static ItemTemplateBaseStat[] BaseStats() =>
    [
        .. Drafted().Select(drafted =>
        {
            (int min, int max) = Spread(drafted.Value,
                drafted.Stat == ItemBaseStat.AttackTime ? AttackTimeSpreadPercent : BaseStatSpreadPercent);
            return new ItemTemplateBaseStat { TemplateId = drafted.TemplateId, Stat = drafted.Stat, Min = min, Max = max };
        }),
    ];

    /// <summary><paramref name="value" /> ± <paramref name="percent" /> %, each end rounded half away from zero and at least 1.</summary>
    internal static (int Min, int Max) Spread(int value, int percent) =>
        (Math.Max(1, RoundHundredths(value * (100 - percent))), Math.Max(1, RoundHundredths(value * (100 + percent))));

    // A non-negative number of hundredths to the nearest whole, half away from zero, in integers so no binary fraction
    // decides a tie (25.5 is 26).
    private static int RoundHundredths(int hundredths) => ((2 * hundredths) + 100) / 200;

    // A base's drafted base stats by its kind and tier (1, 2 or 3), in ItemBaseStat order.
    private static IEnumerable<DraftedBaseStat> DraftedOf(ulong id, ItemBaseKind kind, int tier)
    {
        switch (kind)
        {
            case >= ItemBaseKind.PlateHead and <= ItemBaseKind.ClothFeet:
                yield return ArmourDrafted(id, kind, tier);
                break;
            case ItemBaseKind.Shield:
                yield return new DraftedBaseStat(id, ItemBaseStat.Armour, (int)Pick(tier, 6, 14, 24));
                yield return new DraftedBaseStat(id, ItemBaseStat.BlockChance, (int)Pick(tier, 5, 7, 10));
                break;
            case ItemBaseKind.CasterOffHand:
                yield return new DraftedBaseStat(id, ItemBaseStat.Ward, (int)Pick(tier, 8, 18, 30));
                break;
            case ItemBaseKind.Sword or ItemBaseKind.Mace or ItemBaseKind.Staff or ItemBaseKind.Bow:
                ((int Min, int Max)[] damage, int attackTime) = kind switch
                {
                    ItemBaseKind.Sword => (new[] { (3, 7), (6, 10), (9, 16) }, 700),
                    ItemBaseKind.Bow => (new[] { (4, 7), (7, 11), (11, 17) }, 800),
                    ItemBaseKind.Mace => (new[] { (4, 9), (7, 14), (12, 21) }, 900),
                    _ => (new[] { (5, 9), (8, 15), (13, 22) }, 1000),
                };
                yield return new DraftedBaseStat(id, ItemBaseStat.DamageMin, damage[tier - 1].Min);
                yield return new DraftedBaseStat(id, ItemBaseStat.DamageMax, damage[tier - 1].Max);
                yield return new DraftedBaseStat(id, ItemBaseStat.AttackTime, attackTime);
                break;
        }
    }

    // An armour piece's defence: its family's chest value at its tier, times its slot's share. The kinds run plate,
    // leather, cloth, each head, shoulders, chest, hands, legs, feet.
    private static DraftedBaseStat ArmourDrafted(ulong id, ItemBaseKind kind, int tier)
    {
        int offset = (int)kind - (int)ItemBaseKind.PlateHead;
        (ItemBaseStat stat, uint chest) = (offset / 6) switch
        {
            0 => (ItemBaseStat.Armour, Pick(tier, 8, 18, 32)),
            1 => (ItemBaseStat.Evasion, Pick(tier, 40, 90, 160)),
            _ => (ItemBaseStat.Ward, Pick(tier, 12, 26, 45)),
        };
        int share = (offset % 6) switch
        {
            0 => 60,
            1 => 50,
            2 => 100,
            4 => 80,
            _ => 40,
        };

        return new DraftedBaseStat(id, stat, RoundHundredths((int)chest * share));
    }

    private static int TierOf(ushort? level) => level switch
    {
        Tier1Level => 1,
        Tier2Level => 2,
        Tier3Level => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "A base is seeded at required level 1, 5 or 10."),
    };

    // Tier-1 armour prices; tier 2 doubles them and tier 3 quadruples them.
    private static uint ArmourPrice(ArmourSlot slot, int tier) => (slot switch
    {
        ArmourSlot.Head => 60u,
        ArmourSlot.Chest => 100u,
        ArmourSlot.Legs => 80u,
        ArmourSlot.Shoulders => 50u,
        _ => 40u,
    }) * Pick(tier, 1, 2, 4);

    private static uint Pick(int tier, uint first, uint second, uint third) => tier switch
    {
        1 => first,
        2 => second,
        3 => third,
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };

    private static ushort LevelOf(int tier) => tier switch
    {
        1 => Tier1Level,
        2 => Tier2Level,
        _ => Tier3Level,
    };

    private static ushort? AttributeOf(int tier) => tier switch
    {
        1 => null,
        2 => Tier2Attribute,
        _ => Tier3Attribute,
    };

    private static IEnumerable<ItemTemplate> ArmourSet(Family family, int tier, string[] names, ulong[] ids)
    {
        ArmourSlot[] slots = [ArmourSlot.Head, ArmourSlot.Chest, ArmourSlot.Legs, ArmourSlot.Hands, ArmourSlot.Feet, ArmourSlot.Shoulders];
        (Attribute attribute, ItemBaseKind head) = family switch
        {
            Family.Plate => (Attribute.Strength, ItemBaseKind.PlateHead),
            Family.Leather => (Attribute.Agility, ItemBaseKind.LeatherHead),
            _ => (Attribute.Intellect, ItemBaseKind.ClothHead),
        };

        for (int i = 0; i < slots.Length; i++)
        {
            ArmourSlot slot = slots[i];
            (ItemSubClass subClass, ItemSlotType worn, int kindOffset) = slot switch
            {
                ArmourSlot.Head => (ItemSubClass.Helmet, ItemSlotType.Head, 0),
                ArmourSlot.Shoulders => (ItemSubClass.Shoulders, ItemSlotType.Shoulder, 1),
                ArmourSlot.Chest => (ItemSubClass.Chest, ItemSlotType.Chest, 2),
                ArmourSlot.Hands => (ItemSubClass.Gloves, ItemSlotType.Hands, 3),
                ArmourSlot.Legs => (ItemSubClass.Legs, ItemSlotType.Legs, 4),
                _ => (ItemSubClass.Boots, ItemSlotType.Feet, 5),
            };

            yield return Base(ids[i], names[i], (ItemBaseKind)((int)head + kindOffset), ItemClass.Armor, subClass,
                worn, tier, ArmourPrice(slot, tier), attribute);
        }
    }

    private static ItemTemplate Weapon(
        ulong id, string name, ItemBaseKind kind, ItemSubClass subClass, int tier, uint attackTimeMs,
        params Attribute[] attributes)
    {
        ItemTemplate weapon = Base(id, name, kind, ItemClass.Weapon, subClass, ItemSlotType.MainHand, tier,
            Pick(tier, 120, 240, 480), attributes);
        weapon.DamageMin1 = Pick(tier, 4, 7, 11);
        weapon.DamageMax1 = Pick(tier, 7, 11, 17);
        weapon.DamageType1 = DamageType.Physical;
        weapon.AttackTimeMs = attackTimeMs;
        weapon.RequiresAnyAttribute = attributes.Length > 1;
        return weapon;
    }

    private static ItemTemplate OffHand(
        ulong id, string name, ItemBaseKind kind, ItemSubClass subClass, int tier, Attribute attribute) =>
        Base(id, name, kind, ItemClass.Armor, subClass, ItemSlotType.OffHand, tier, Pick(tier, 120, 240, 480), attribute);

    // A ring or amulet line's three tiers, which ask no attribute and cost 200, 400 and 800.
    private static IEnumerable<ItemTemplate> JewelleryLine(ItemBaseKind kind, (StatType Stat, ulong[] Ids, string[] Names) line)
    {
        (ItemSubClass subClass, ItemSlotType slot) = kind == ItemBaseKind.Ring
            ? (ItemSubClass.Ring, ItemSlotType.Finger)
            : (ItemSubClass.Amulet, ItemSlotType.Neck);
        for (int tier = 1; tier <= line.Ids.Length; tier++)
        {
            yield return Base(line.Ids[tier - 1], line.Names[tier - 1], kind, ItemClass.Armor, subClass, slot, tier,
                Pick(tier, 200, 400, 800));
        }
    }

    private static ItemTemplate Base(
        ulong id, string name, ItemBaseKind kind, ItemClass itemClass, ItemSubClass subClass, ItemSlotType slot, int tier,
        uint buyPrice, params Attribute[] attributes)
    {
        ItemTemplate template = Equipment(id, name, kind, itemClass, subClass, slot, ItemRarity.Common, buyPrice,
            LevelOf(tier));
        ushort? amount = AttributeOf(tier);
        foreach (Attribute attribute in attributes)
        {
            switch (attribute)
            {
                case Attribute.Strength:
                    template.RequiredStrength = amount;
                    break;
                case Attribute.Agility:
                    template.RequiredAgility = amount;
                    break;
                default:
                    template.RequiredIntellect = amount;
                    break;
            }
        }

        return template;
    }

    private static ItemTemplate Unique(
        ulong id, string name, ItemBaseKind kind, ItemSubClass subClass, ItemSlotType slot, ushort level, uint sellPrice,
        ulong baseTemplateId)
    {
        ItemTemplate unique = Equipment(id, name, kind, ItemClass.Armor, subClass, slot, ItemRarity.Unique, 4 * sellPrice, level);
        unique.BaseTemplateId = new ItemTemplateId(baseTemplateId);
        return unique;
    }

    private static ItemTemplate Equipment(
        ulong id, string name, ItemBaseKind kind, ItemClass itemClass, ItemSubClass subClass, ItemSlotType slot,
        ItemRarity rarity, uint buyPrice, ushort level) => new()
        {
            Id = id,
            Name = name,
            Class = itemClass,
            SubClass = subClass,
            Flags = ItemTemplateFlags.None,
            MaxStackSize = 1,
            DisplayId = (uint)id,
            Rarity = rarity,
            BaseKind = kind,
            BuyPrice = buyPrice,
            SellPrice = buyPrice / 4,
            Slot = slot,
            RequiredLevel = level,
        };
}
