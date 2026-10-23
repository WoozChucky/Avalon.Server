using System.ComponentModel.DataAnnotations;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Combat;

namespace Avalon.Domain.World;

public class ItemTemplate : IDbEntity<ItemTemplateId>
{
    [Key]
    public ItemTemplateId Id { get; set; }

    public string Name { get; set; }

    public ItemClass Class { get; set; }

    public ItemSubClass SubClass { get; set; }

    public ItemTemplateFlags Flags { get; set; }

    public bool Stackable => MaxStackSize > 1;

    /// <summary>True when a character of this class may equip the item: the list is empty or names the class.</summary>
    public bool AllowsClass(CharacterClass characterClass) =>
        AllowedClasses.Count == 0 || AllowedClasses.Contains(characterClass);

    /// <summary>
    /// True when every attribute requirement the item sets (#1178) is at most the matching value; an unset one asks
    /// nothing.
    /// </summary>
    public bool MeetsAttributes(uint strength, uint agility, uint intellect) =>
        (RequiredStrength is not { } str || str <= strength)
        && (RequiredAgility is not { } agi || agi <= agility)
        && (RequiredIntellect is not { } intl || intl <= intellect);

    public uint MaxStackSize { get; set; }

    public uint DisplayId { get; set; }

    public ItemRarity Rarity { get; set; }

    public uint BuyPrice { get; set; }

    public uint SellPrice { get; set; }

    public ItemSlotType? Slot { get; set; }

    /// <summary>
    /// The classes that may equip the item; a use is not checked against it. Empty when every class may (#1171): an item
    /// lists classes only when it is restricted to some of them. See <see cref="AllowsClass"/>.
    /// </summary>
    public List<CharacterClass> AllowedClasses { get; set; } = [];

    public ushort? ItemPower { get; set; }

    public ushort? RequiredLevel { get; set; }

    /// <summary>The Strength a character needs to equip the item (#1178), or null for none.</summary>
    public ushort? RequiredStrength { get; set; }

    /// <summary>The Agility a character needs to equip the item, or null for none.</summary>
    public ushort? RequiredAgility { get; set; }

    /// <summary>The Intellect a character needs to equip the item, or null for none.</summary>
    public ushort? RequiredIntellect { get; set; }

    public uint? DamageMin1 { get; set; }

    public uint? DamageMax1 { get; set; }

    public DamageType? DamageType1 { get; set; }

    public uint? DamageMin2 { get; set; }

    public uint? DamageMax2 { get; set; }

    public DamageType? DamageType2 { get; set; }

    /// <summary>
    /// Milliseconds between a weapon's basic attacks before attack speed (#1178), above 0; null for an item that is not
    /// a weapon.
    /// </summary>
    public uint? AttackTimeMs { get; set; }

    public StatType? StatType1 { get; set; }

    public uint? StatValue1 { get; set; }

    public StatType? StatType2 { get; set; }

    public uint? StatValue2 { get; set; }

    public StatType? StatType3 { get; set; }

    public uint? StatValue3 { get; set; }

    public StatType? StatType4 { get; set; }

    public uint? StatValue4 { get; set; }

    public StatType? StatType5 { get; set; }

    public uint? StatValue5 { get; set; }

    public StatType? StatType6 { get; set; }

    public uint? StatValue6 { get; set; }

    public StatType? StatType7 { get; set; }

    public uint? StatValue7 { get; set; }

    public StatType? StatType8 { get; set; }

    public uint? StatValue8 { get; set; }

    public StatType? StatType9 { get; set; }

    public uint? StatValue9 { get; set; }

    public StatType? StatType10 { get; set; }

    public uint? StatValue10 { get; set; }

    /// <summary>
    /// The ItemScript a use runs (item use), by class name, or null for an item a use cannot run. An item
    /// whose Slot is worn somewhere is equipped by a use instead and never runs one.
    /// </summary>
    public string? UseScript { get; set; }

    /// <summary>Milliseconds of cast bar before the script runs; null or 0 runs it at once.</summary>
    public uint? UseCastTimeMs { get; set; }

    /// <summary>Milliseconds before this item can be used again once a use succeeded; null for none.</summary>
    public uint? UseCooldownMs { get; set; }

    /// <summary>Items naming the same group share one cooldown (the potions' "potion"); null for none.</summary>
    public string? UseCooldownGroup { get; set; }

    /// <summary>The script's own number: for RestoreHealth and RestorePower, the percentage of the maximum restored.</summary>
    public uint? UseValue { get; set; }
}

/// <summary>A stat an item gives: the stored numbers, the retired ones (8 and 9, the old damage stats) never reused.</summary>
public enum StatType
{
    Stamina = 0,
    Strength = 1,
    Agility = 2,
    Intellect = 3,
    Armor = 4,
    BlockPct = 5,

    /// <summary>Evasion rating (#1178), formerly the dodge chance.</summary>
    Evasion = 6,
    CritPct = 7,
    Health = 10,
    Power = 11,
    AttackSpeed = 12,
    MovementSpeed = 13,
    Ward = 14,
    FireResistance = 15,
    ColdResistance = 16,
    LightningResistance = 17,
    BlightResistance = 18,

    // The offence model (#1178): percentages of increased damage or healing, then the crit multiplier and the speeds.
    IncreasedDamage = 19,
    IncreasedPhysicalDamage = 20,
    IncreasedFireDamage = 21,
    IncreasedColdDamage = 22,
    IncreasedLightningDamage = 23,
    IncreasedBlightDamage = 24,
    IncreasedAttackDamage = 25,
    IncreasedSpellDamage = 26,
    IncreasedAreaDamage = 27,
    IncreasedProjectileDamage = 28,
    IncreasedMeleeDamage = 29,
    IncreasedDamageOverTime = 30,
    IncreasedHealing = 31,
    CritMultiplier = 32,
    CastSpeed = 33,
    CooldownRecovery = 34,
}

[Flags]
public enum ItemTemplateFlags
{
    None = 0,
    Attunned = 1,
    AccountAttunned = 2,
    Unique = 4,
    UniqueEquipped = 8,
    AttuneOnPickup = 16,
    AttuneOnEquip = 32,
    AttuneOnUse = 64,
    AttuneOnAccount = 128,
    NoSell = 256,
    NoDestroy = 512,
    NoTrade = 1024,

    /// <summary>
    /// A quest item (#433): counted by Collect objectives, dropped only for characters who need it, never sold,
    /// and taken back on turn-in or abandon.
    /// </summary>
    QuestItem = 2048,
}

public enum ItemSlotType : ushort
{
    Head,
    Neck,
    Shoulder,
    Chest,
    Hands,
    Legs,
    Feet,
    Finger,
    Gem,
    MainHand,
    OffHand
}

public enum ItemRarity
{
    Junk,
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary
}

public enum ItemClass
{
    Consumable,
    Weapon,
    Armor,
    Quest,
    Crafting,
    Junk
}

public enum ItemSubClass
{
    // Consumable Subclasses
    Potion = 0,
    Food = 1,
    Scroll = 2,
    // Weapon Subclasses
    OneHanded = 100,
    TwoHanded = 101,
    Ranged = 102,
    // Armor Subclasses
    Shield = 200,
    Helmet = 201,
    Chest = 202,
    Legs = 203,
    Boots = 204,
    Gloves = 205,

    /// <summary>A neck piece (#433: the Alpha's Fang Pendant). Worn in EquipmentSlots.Neck.</summary>
    Amulet = 206,

    /// <summary>A ring (forest content pass: the Heartwood Band and the Thornheart Signet). Worn in EquipmentSlots.Finger1/Finger2.</summary>
    Ring = 207,
    // Quest Subclasses
    QuestItem = 300,
    // Crafting Subclasses
    CraftingMaterial = 400,
    // Junk Subclasses
    JunkItem = 500
}
