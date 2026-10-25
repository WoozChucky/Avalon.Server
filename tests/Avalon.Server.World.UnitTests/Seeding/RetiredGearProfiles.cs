using Avalon.Combat;
using Avalon.Common;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;

namespace Avalon.Server.World.UnitTests.Seeding;

/// <summary>
/// The balance tool's starter and forest gear profiles as the seed held them before #1179 replaced them with bases: each
/// class's weapon and five armour pieces, and the starter Warrior with the Rusted Sword. The #1178 carry-over theories
/// pin today's numbers through these rows, so they keep the old rows' values (the fields the stats read: slot, class,
/// damage, attack time and stat lines) once the seeded rows are gone. Each is worn as a copy: a weapon's damage and attack
/// time are the base stats its copy rolled (#1179), as a base carries them now, at the old row's values.
/// </summary>
internal static class RetiredGearProfiles
{
    private static readonly Dictionary<string, Dictionary<CharacterClass, ulong[]>> s_profiles = new()
    {
        ["starter"] = new()
        {
            [CharacterClass.Warrior] = [32, 36, 37, 38, 39, 40],
            [CharacterClass.Wizard] = [33, 41, 42, 43, 44, 45],
            [CharacterClass.Hunter] = [34, 46, 47, 48, 49, 50],
            [CharacterClass.Cleric] = [35, 51, 52, 53, 54, 55],
        },
        ["forest"] = new()
        {
            [CharacterClass.Warrior] = [7, 12, 13, 14, 15, 16],
            [CharacterClass.Hunter] = [6, 22, 23, 24, 25, 26],
        },
        ["rusted"] = new() { [CharacterClass.Warrior] = [4, 36, 37, 38, 39, 40] },
    };

    /// <summary>The six retired rows <paramref name="profile" /> gave <paramref name="characterClass" />, made fresh and worn.</summary>
    public static List<WornItem> For(string profile, CharacterClass characterClass) =>
        s_profiles[profile][characterClass].Select(Row).ToList();

    private static WornItem Row(ulong id) => id switch
    {
        4 => Weapon(4, "Rusted Sword", CharacterClass.Warrior, ItemSubClass.OneHanded, 4, 6, 792, null),
        6 => Weapon(6, "Briarstring Bow", CharacterClass.Hunter, ItemSubClass.Ranged, 5, 7, 801, StatType.Agility),
        7 => Weapon(7, "Bramblesteel Sword", CharacterClass.Warrior, ItemSubClass.OneHanded, 5, 7, 792, StatType.Strength),
        12 => Armour(12, "Barkplate Helm", CharacterClass.Warrior, ItemSubClass.Helmet, ItemSlotType.Head, (StatType.Strength, 1), (StatType.Armor, 4), (StatType.Stamina, 1)),
        13 => Armour(13, "Barkplate Chestguard", CharacterClass.Warrior, ItemSubClass.Chest, ItemSlotType.Chest, (StatType.Strength, 2), (StatType.Armor, 8), (StatType.Stamina, 2)),
        14 => Armour(14, "Barkplate Legguards", CharacterClass.Warrior, ItemSubClass.Legs, ItemSlotType.Legs, (StatType.Strength, 2), (StatType.Armor, 6), (StatType.Stamina, 1)),
        15 => Armour(15, "Barkplate Gauntlets", CharacterClass.Warrior, ItemSubClass.Gloves, ItemSlotType.Hands, (StatType.Strength, 1), (StatType.Armor, 3), (StatType.Stamina, 1)),
        16 => Armour(16, "Barkplate Boots", CharacterClass.Warrior, ItemSubClass.Boots, ItemSlotType.Feet, (StatType.Strength, 1), (StatType.Armor, 3), (StatType.Stamina, 1)),
        22 => Armour(22, "Fernstalker Cap", CharacterClass.Hunter, ItemSubClass.Helmet, ItemSlotType.Head, (StatType.Agility, 2), (StatType.Armor, 2)),
        23 => Armour(23, "Fernstalker Jerkin", CharacterClass.Hunter, ItemSubClass.Chest, ItemSlotType.Chest, (StatType.Agility, 3), (StatType.Armor, 5)),
        24 => Armour(24, "Fernstalker Breeches", CharacterClass.Hunter, ItemSubClass.Legs, ItemSlotType.Legs, (StatType.Agility, 3), (StatType.Armor, 4)),
        25 => Armour(25, "Fernstalker Grips", CharacterClass.Hunter, ItemSubClass.Gloves, ItemSlotType.Hands, (StatType.Agility, 1), (StatType.Armor, 2)),
        26 => Armour(26, "Fernstalker Boots", CharacterClass.Hunter, ItemSubClass.Boots, ItemSlotType.Feet, (StatType.Agility, 1), (StatType.Armor, 2)),
        32 => Weapon(32, "Ironwood Sword", CharacterClass.Warrior, ItemSubClass.OneHanded, 4, 7, 792, StatType.Strength),
        33 => Weapon(33, "Ash Staff", CharacterClass.Wizard, ItemSubClass.TwoHanded, 4, 7, 800, StatType.Intellect),
        34 => Weapon(34, "Hunter's Shortbow", CharacterClass.Hunter, ItemSubClass.Ranged, 4, 7, 801, StatType.Agility),
        35 => Weapon(35, "Oak Mace", CharacterClass.Cleric, ItemSubClass.OneHanded, 4, 7, 784, StatType.Intellect),
        36 => Armour(36, "Ironbound Helm", CharacterClass.Warrior, ItemSubClass.Helmet, ItemSlotType.Head, (StatType.Strength, 1), (StatType.Armor, 2), (StatType.Stamina, 1)),
        37 => Armour(37, "Ironbound Chestguard", CharacterClass.Warrior, ItemSubClass.Chest, ItemSlotType.Chest, (StatType.Strength, 1), (StatType.Armor, 5), (StatType.Stamina, 1)),
        38 => Armour(38, "Ironbound Legguards", CharacterClass.Warrior, ItemSubClass.Legs, ItemSlotType.Legs, (StatType.Strength, 1), (StatType.Armor, 4), (StatType.Stamina, 1)),
        39 => Armour(39, "Ironbound Gauntlets", CharacterClass.Warrior, ItemSubClass.Gloves, ItemSlotType.Hands, (StatType.Strength, 1), (StatType.Armor, 2), (StatType.Stamina, 1)),
        40 => Armour(40, "Ironbound Boots", CharacterClass.Warrior, ItemSubClass.Boots, ItemSlotType.Feet, (StatType.Strength, 1), (StatType.Armor, 2), (StatType.Stamina, 1)),
        41 => Armour(41, "Linen Hood", CharacterClass.Wizard, ItemSubClass.Helmet, ItemSlotType.Head, (StatType.Intellect, 1), (StatType.Armor, 1)),
        42 => Armour(42, "Linen Robe", CharacterClass.Wizard, ItemSubClass.Chest, ItemSlotType.Chest, (StatType.Intellect, 2), (StatType.Armor, 2)),
        43 => Armour(43, "Linen Leggings", CharacterClass.Wizard, ItemSubClass.Legs, ItemSlotType.Legs, (StatType.Intellect, 2), (StatType.Armor, 1)),
        44 => Armour(44, "Linen Gloves", CharacterClass.Wizard, ItemSubClass.Gloves, ItemSlotType.Hands, (StatType.Intellect, 1), (StatType.Armor, 1)),
        45 => Armour(45, "Linen Slippers", CharacterClass.Wizard, ItemSubClass.Boots, ItemSlotType.Feet, (StatType.Intellect, 1), (StatType.Armor, 1)),
        46 => Armour(46, "Hide Cap", CharacterClass.Hunter, ItemSubClass.Helmet, ItemSlotType.Head, (StatType.Agility, 1), (StatType.Armor, 1)),
        47 => Armour(47, "Hide Jerkin", CharacterClass.Hunter, ItemSubClass.Chest, ItemSlotType.Chest, (StatType.Agility, 2), (StatType.Armor, 3)),
        48 => Armour(48, "Hide Breeches", CharacterClass.Hunter, ItemSubClass.Legs, ItemSlotType.Legs, (StatType.Agility, 2), (StatType.Armor, 2)),
        49 => Armour(49, "Hide Grips", CharacterClass.Hunter, ItemSubClass.Gloves, ItemSlotType.Hands, (StatType.Agility, 1), (StatType.Armor, 1)),
        50 => Armour(50, "Hide Boots", CharacterClass.Hunter, ItemSubClass.Boots, ItemSlotType.Feet, (StatType.Agility, 1), (StatType.Armor, 1)),
        51 => Armour(51, "Wool Circlet", CharacterClass.Cleric, ItemSubClass.Helmet, ItemSlotType.Head, (StatType.Intellect, 1), (StatType.Stamina, 1), (StatType.Armor, 1)),
        52 => Armour(52, "Wool Vestments", CharacterClass.Cleric, ItemSubClass.Chest, ItemSlotType.Chest, (StatType.Intellect, 1), (StatType.Stamina, 1), (StatType.Armor, 2)),
        53 => Armour(53, "Wool Leggings", CharacterClass.Cleric, ItemSubClass.Legs, ItemSlotType.Legs, (StatType.Intellect, 1), (StatType.Stamina, 1), (StatType.Armor, 1)),
        54 => Armour(54, "Wool Handwraps", CharacterClass.Cleric, ItemSubClass.Gloves, ItemSlotType.Hands, (StatType.Intellect, 1), (StatType.Stamina, 1), (StatType.Armor, 1)),
        55 => Armour(55, "Wool Sandals", CharacterClass.Cleric, ItemSubClass.Boots, ItemSlotType.Feet, (StatType.Intellect, 1), (StatType.Stamina, 1), (StatType.Armor, 1)),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "no retired profile row"),
    };

    private static WornItem Weapon(
        ulong id, string name, CharacterClass characterClass, ItemSubClass subClass, int min, int max, int attackTimeMs,
        StatType? attribute) => new(
        new ItemTemplate
        {
            Id = id,
            Name = name,
            Class = ItemClass.Weapon,
            SubClass = subClass,
            MaxStackSize = 1,
            Slot = ItemSlotType.MainHand,
            AllowedClasses = [characterClass],
            RequiredLevel = 1,
            DamageType = DamageType.Physical,
            StatType1 = attribute,
            StatValue1 = attribute is null ? null : 1,
        },
        new ItemBaseStats { DamageMin = min, DamageMax = max, AttackTime = attackTimeMs },
        null,
        DamageType.Physical);

    private static WornItem Armour(
        ulong id, string name, CharacterClass characterClass, ItemSubClass subClass, ItemSlotType slot,
        params (StatType Type, uint Value)[] stats) => new(
        new ItemTemplate
        {
            Id = id,
            Name = name,
            Class = ItemClass.Armor,
            SubClass = subClass,
            MaxStackSize = 1,
            Slot = slot,
            AllowedClasses = [characterClass],
            RequiredLevel = 1,
            StatType1 = stats[0].Type,
            StatValue1 = stats[0].Value,
            StatType2 = stats[1].Type,
            StatValue2 = stats[1].Value,
            StatType3 = stats.Length > 2 ? stats[2].Type : null,
            StatValue3 = stats.Length > 2 ? stats[2].Value : null,
        },
        default,
        null,
        null);
}
