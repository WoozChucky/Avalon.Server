using Avalon.Common;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;

namespace Avalon.Database.World.Seeding;

/// <summary>
/// The seeded equipment (#1179): 74 bases and 5 uniques. A base is Common, lists no classes and carries no attributes:
/// its defence or damage, its implicit lines and its requirements, in three tiers at required levels 1, 5 and 10 (the
/// attribute asks none, 30 and 45). Plate gives armour and asks Strength, leather evasion and Agility, cloth ward and
/// Intellect. A unique is seeded without stat lines: its stats are its unique modifier, rolled per copy. Numbers are
/// rounded half away from zero and at least 1; every base sells for a quarter of its price. The model's HasData reads
/// these; consumables and quest items stay in WorldDbContext, with no base kind.
/// </summary>
public static class ItemBaseSeed
{
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
        // Plate: armour, Strength. Head, chest, legs, hands, feet, shoulders; tiers 1, 2 and 3.
        .. ArmourSet(Family.Plate, 1, ["Ironbound Helm", "Ironbound Chestguard", "Ironbound Legguards", "Ironbound Gauntlets", "Ironbound Boots", "Ironbound Pauldrons"], [36, 37, 38, 39, 40, 76]),
        .. ArmourSet(Family.Plate, 2, ["Barkplate Helm", "Barkplate Chestguard", "Barkplate Legguards", "Barkplate Gauntlets", "Barkplate Boots", "Barkplate Pauldrons"], [12, 13, 14, 15, 16, 79]),
        .. ArmourSet(Family.Plate, 3, ["Ironroot Helm", "Ironroot Chestguard", "Ironroot Legguards", "Ironroot Gauntlets", "Ironroot Boots", "Ironroot Pauldrons"], [85, 86, 87, 88, 89, 82]),

        // Leather: evasion, Agility.
        .. ArmourSet(Family.Leather, 1, ["Hide Cap", "Hide Jerkin", "Hide Breeches", "Hide Grips", "Hide Boots", "Hide Mantle"], [46, 47, 48, 49, 50, 77]),
        .. ArmourSet(Family.Leather, 2, ["Fernstalker Cap", "Fernstalker Jerkin", "Fernstalker Breeches", "Fernstalker Grips", "Fernstalker Boots", "Fernstalker Mantle"], [22, 23, 24, 25, 26, 80]),
        .. ArmourSet(Family.Leather, 3, ["Gloamhide Cap", "Gloamhide Jerkin", "Gloamhide Breeches", "Gloamhide Grips", "Gloamhide Boots", "Gloamhide Mantle"], [90, 91, 92, 93, 94, 83]),

        // Cloth: ward, Intellect.
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

        // Off hands: a shield's armour and block, Strength; a caster off-hand's ward and increased spell damage, Intellect.
        OffHand(70, "Oak Buckler", ItemBaseKind.Shield, ItemSubClass.Shield, 1, Attribute.Strength, (StatType.Armor, 6), (StatType.BlockPct, 5)),
        OffHand(71, "Barkplate Shield", ItemBaseKind.Shield, ItemSubClass.Shield, 2, Attribute.Strength, (StatType.Armor, 14), (StatType.BlockPct, 7)),
        OffHand(72, "Ironroot Kite Shield", ItemBaseKind.Shield, ItemSubClass.Shield, 3, Attribute.Strength, (StatType.Armor, 24), (StatType.BlockPct, 10)),
        OffHand(73, "Ash Focus", ItemBaseKind.CasterOffHand, ItemSubClass.CasterOffHand, 1, Attribute.Intellect, (StatType.Ward, 8), (StatType.IncreasedSpellDamage, 5)),
        OffHand(74, "Thornwood Focus", ItemBaseKind.CasterOffHand, ItemSubClass.CasterOffHand, 2, Attribute.Intellect, (StatType.Ward, 18), (StatType.IncreasedSpellDamage, 8)),
        OffHand(75, "Gloamwood Orb", ItemBaseKind.CasterOffHand, ItemSubClass.CasterOffHand, 3, Attribute.Intellect, (StatType.Ward, 30), (StatType.IncreasedSpellDamage, 12)),

        // Jewellery: one base each, level 1, no implicit.
        Jewellery(68, "Copper Band", ItemBaseKind.Ring, ItemSubClass.Ring, ItemSlotType.Finger),
        Jewellery(69, "Bone Charm", ItemBaseKind.Amulet, ItemSubClass.Amulet, ItemSlotType.Neck),

        // The uniques: hand-made, a required level and prices only. Their stats are their unique modifiers, rolled per
        // copy, which the modifier catalog brings; until then nothing drops, sells or rewards them.
        Unique(58, "Alpha's Fang Pendant", ItemBaseKind.Amulet, ItemSubClass.Amulet, ItemSlotType.Neck, 3, 50),
        Unique(64, "Heartwood Band", ItemBaseKind.Ring, ItemSubClass.Ring, ItemSlotType.Finger, 4, 100),
        Unique(65, "Thornheart Signet", ItemBaseKind.Ring, ItemSubClass.Ring, ItemSlotType.Finger, 5, 200),
        Unique(66, "Bramble Mother's Seed", ItemBaseKind.Trinket, ItemSubClass.Trinket, ItemSlotType.Trinket, 5, 200),
        Unique(67, "Tuskroot Idol", ItemBaseKind.Trinket, ItemSubClass.Trinket, ItemSlotType.Trinket, 4, 100),
    ];

    // Chest defence by family and tier; the other slots are shares of it (legs 80 %, head 60 %, shoulders 50 %, hands and
    // feet 40 %), written out as rounded.
    private static uint Defence(Family family, ArmourSlot slot, int tier) => (family, slot) switch
    {
        (Family.Plate, ArmourSlot.Chest) => Pick(tier, 8, 18, 32),
        (Family.Plate, ArmourSlot.Legs) => Pick(tier, 6, 14, 26),
        (Family.Plate, ArmourSlot.Head) => Pick(tier, 5, 11, 19),
        (Family.Plate, ArmourSlot.Shoulders) => Pick(tier, 4, 9, 16),
        (Family.Plate, _) => Pick(tier, 3, 7, 13),
        (Family.Leather, ArmourSlot.Chest) => Pick(tier, 40, 90, 160),
        (Family.Leather, ArmourSlot.Legs) => Pick(tier, 32, 72, 128),
        (Family.Leather, ArmourSlot.Head) => Pick(tier, 24, 54, 96),
        (Family.Leather, ArmourSlot.Shoulders) => Pick(tier, 20, 45, 80),
        (Family.Leather, _) => Pick(tier, 16, 36, 64),
        (Family.Cloth, ArmourSlot.Chest) => Pick(tier, 12, 26, 45),
        (Family.Cloth, ArmourSlot.Legs) => Pick(tier, 10, 21, 36),
        (Family.Cloth, ArmourSlot.Head) => Pick(tier, 7, 16, 27),
        (Family.Cloth, ArmourSlot.Shoulders) => Pick(tier, 6, 13, 23),
        (Family.Cloth, _) => Pick(tier, 5, 10, 18),
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
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
        (StatType defence, Attribute attribute, ItemBaseKind head) = family switch
        {
            Family.Plate => (StatType.Armor, Attribute.Strength, ItemBaseKind.PlateHead),
            Family.Leather => (StatType.Evasion, Attribute.Agility, ItemBaseKind.LeatherHead),
            _ => (StatType.Ward, Attribute.Intellect, ItemBaseKind.ClothHead),
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

            ItemTemplate piece = Base(ids[i], names[i], (ItemBaseKind)((int)head + kindOffset), ItemClass.Armor, subClass,
                worn, tier, ArmourPrice(slot, tier), attribute);
            piece.StatType1 = defence;
            piece.StatValue1 = Defence(family, slot, tier);
            yield return piece;
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
        ulong id, string name, ItemBaseKind kind, ItemSubClass subClass, int tier, Attribute attribute,
        (StatType Type, uint Value) first, (StatType Type, uint Value) second)
    {
        ItemTemplate offHand = Base(id, name, kind, ItemClass.Armor, subClass, ItemSlotType.OffHand, tier,
            Pick(tier, 120, 240, 480), attribute);
        offHand.StatType1 = first.Type;
        offHand.StatValue1 = first.Value;
        offHand.StatType2 = second.Type;
        offHand.StatValue2 = second.Value;
        return offHand;
    }

    private static ItemTemplate Jewellery(ulong id, string name, ItemBaseKind kind, ItemSubClass subClass, ItemSlotType slot) =>
        Base(id, name, kind, ItemClass.Armor, subClass, slot, tier: 1, buyPrice: 200);

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
        ulong id, string name, ItemBaseKind kind, ItemSubClass subClass, ItemSlotType slot, ushort level, uint sellPrice) =>
        Equipment(id, name, kind, ItemClass.Armor, subClass, slot, ItemRarity.Unique, 4 * sellPrice, level);

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
