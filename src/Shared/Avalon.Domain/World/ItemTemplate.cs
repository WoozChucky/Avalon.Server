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

    /// <summary>
    /// True for a weapon held in both hands, which leaves the off hand empty (#1179): a two-handed weapon or a ranged one
    /// (a bow).
    /// </summary>
    public bool IsTwoHanded => SubClass is ItemSubClass.TwoHanded or ItemSubClass.Ranged;

    /// <summary>True when a character of this class may equip the item: the list is empty or names the class.</summary>
    public bool AllowsClass(CharacterClass characterClass) =>
        AllowedClasses.Count == 0 || AllowedClasses.Contains(characterClass);

    /// <summary>
    /// True when the character meets the item's attribute requirements (#1178): every one it sets, or, when
    /// <see cref="RequiresAnyAttribute" /> is set (#1179), at least one of them. An unset requirement asks nothing, and an
    /// item that sets none is always met.
    /// </summary>
    public bool MeetsAttributes(uint strength, uint agility, uint intellect)
    {
        if (RequiredStrength is null && RequiredAgility is null && RequiredIntellect is null)
            return true;

        if (RequiresAnyAttribute)
        {
            return (RequiredStrength is { } anyStr && anyStr <= strength)
                || (RequiredAgility is { } anyAgi && anyAgi <= agility)
                || (RequiredIntellect is { } anyIntl && anyIntl <= intellect);
        }

        return (RequiredStrength is not { } str || str <= strength)
            && (RequiredAgility is not { } agi || agi <= agility)
            && (RequiredIntellect is not { } intl || intl <= intellect);
    }

    public uint MaxStackSize { get; set; }

    public uint DisplayId { get; set; }

    /// <summary>Common for a base, Unique for a hand-made item (#1179); Magic and Rare exist only on an instance.</summary>
    public ItemRarity Rarity { get; set; }

    /// <summary>
    /// The kind of base this template is (#1179): an armour family in one of its slots, a weapon, an off-hand, a piece of
    /// jewellery or a trinket. Null for an item that is no base (a consumable, a quest item).
    /// </summary>
    public ItemBaseKind? BaseKind { get; set; }

    /// <summary>
    /// The base a Unique template is made on (#1179): a Common template of its own kind, at or under its required level,
    /// whose base-stat ranges and implicit every copy of the unique rolls before its own modifier. Set on every Unique
    /// template and on no other.
    /// </summary>
    public ItemTemplateId? BaseTemplateId { get; set; }

    public uint BuyPrice { get; set; }

    public uint SellPrice { get; set; }

    public ItemSlotType? Slot { get; set; }

    /// <summary>
    /// The classes that may equip the item; a use is not checked against it. Empty when every class may (#1171): an item
    /// lists classes only when it is restricted to some of them. See <see cref="AllowsClass"/>.
    /// </summary>
    public List<CharacterClass> AllowedClasses { get; set; } = [];

    public ushort? RequiredLevel { get; set; }

    /// <summary>The Strength a character needs to equip the item (#1178), or null for none.</summary>
    public ushort? RequiredStrength { get; set; }

    /// <summary>The Agility a character needs to equip the item, or null for none.</summary>
    public ushort? RequiredAgility { get; set; }

    /// <summary>The Intellect a character needs to equip the item, or null for none.</summary>
    public ushort? RequiredIntellect { get; set; }

    /// <summary>
    /// When set (#1179), meeting any one attribute requirement is enough, not every one: the mace's Strength or
    /// Intellect. See <see cref="MeetsAttributes" />.
    /// </summary>
    public bool RequiresAnyAttribute { get; set; }

    /// <summary>
    /// The type of a weapon's damage (#1178): set on a template of a weapon kind and on no other (#1179,
    /// <see cref="ItemBaseStatRules" />). A weapon's damage range and attack time are base stats, which every copy rolls.
    /// </summary>
    public DamageType? DamageType { get; set; }

    /// <summary>
    /// The base stats every copy rolls (#1179), one authored range per stat: exactly what
    /// <see cref="ItemBaseStatRules.Of" /> asks of the template's kind and rarity, so empty for jewellery, a trinket, a
    /// unique and an item that is no base. Loaded with the template.
    /// </summary>
    public List<ItemTemplateBaseStat> BaseStats { get; set; } = [];

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

    /// <summary>
    /// Items naming the same group share one cooldown (the health potions' "potion", the Mana Potion's "mana-potion");
    /// null for none.
    /// </summary>
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

    // Item modifiers (#1179). Local to the weapon they sit on: added damage by type, a range.
    AddedPhysicalDamage = 35,
    AddedFireDamage = 36,
    AddedColdDamage = 37,
    AddedLightningDamage = 38,
    AddedBlightDamage = 39,

    // Local to the item they sit on: a weapon's physical damage and attack speed, a piece's own defence.
    LocalIncreasedPhysicalDamage = 40,
    LocalIncreasedArmor = 41,
    LocalIncreasedEvasion = 42,
    LocalIncreasedWard = 43,
    LocalAttackSpeed = 44,

    // Global added damage, a range: to every attack, by type, then to every spell, by element.
    AddedPhysicalDamageToAttacks = 45,
    AddedFireDamageToAttacks = 46,
    AddedColdDamageToAttacks = 47,
    AddedLightningDamageToAttacks = 48,
    AddedBlightDamageToAttacks = 49,
    AddedFireDamageToSpells = 50,
    AddedColdDamageToSpells = 51,
    AddedLightningDamageToSpells = 52,

    // A percentage of increased maximum life: it scales the flat life from base, Stamina and gear (#1179).
    IncreasedMaximumLife = 53,

    // Mana (#1181): a percentage of increased maximum mana, which scales the flat mana from the formula and gear, and a
    // percentage of increased mana regeneration, which scales both regeneration rates.
    IncreasedMaximumMana = 54,
    IncreasedManaRegeneration = 55,
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
    OffHand,

    /// <summary>A trinket (#1179), worn in EquipmentSlots.Trinket1 or Trinket2.</summary>
    Trinket = 11,
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

    /// <summary>A neck piece (#433; #1179: the amulet base and the Alpha's Fang Pendant). Worn in EquipmentSlots.Neck.</summary>
    Amulet = 206,

    /// <summary>A ring (#1179: the ring base and the unique rings). Worn in EquipmentSlots.Finger1/Finger2.</summary>
    Ring = 207,

    /// <summary>A shoulder piece (#1179), the sixth armour slot.</summary>
    Shoulders = 208,

    /// <summary>An off-hand focus or orb for casters (#1179): Armor class, worn in the off hand.</summary>
    CasterOffHand = 209,

    /// <summary>A trinket (#1179), only ever a unique.</summary>
    Trinket = 210,
    // Quest Subclasses
    QuestItem = 300,
    // Crafting Subclasses
    CraftingMaterial = 400,

    /// <summary>
    /// A glyph (#1180 part C): one affix kept from an item destroyed at the Resonance Workbench, carried as the copy's one
    /// modifier row and imprinted onto another item there. The seed holds one template of it, found by this subclass.
    /// </summary>
    Glyph = 401,
    // Junk Subclasses
    JunkItem = 500
}
