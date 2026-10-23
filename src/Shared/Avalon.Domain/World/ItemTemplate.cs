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

    /// <summary>
    /// The rolls an instance made from this template outright gets (#1179): a vendor's, a quest's, a starting item's.
    /// Null for an item that is not worn (no <see cref="Slot" />). A Common template's are the shared plain rolls of its
    /// required level; a Unique's copy its lines at their drafted values (modifier 0, tier 0), made per call.
    /// </summary>
    public ItemRolls? PlainRolls()
    {
        if (Slot is null)
            return null;

        ushort level = RequiredLevel ?? 1;
        if (Rarity != ItemRarity.Unique)
            return ItemRolls.Plain(Rarity, level);

        Span<ItemModifierRoll> lines = stackalloc ItemModifierRoll[ItemRolls.MaxModifiers];
        int count = 0;
        AddLine(lines, ref count, StatType1, StatValue1, StatMax1);
        AddLine(lines, ref count, StatType2, StatValue2, StatMax2);
        AddLine(lines, ref count, StatType3, StatValue3, StatMax3);
        AddLine(lines, ref count, StatType4, StatValue4, StatMax4);
        AddLine(lines, ref count, StatType5, StatValue5, StatMax5);
        AddLine(lines, ref count, StatType6, StatValue6, StatMax6);
        AddLine(lines, ref count, StatType7, StatValue7, StatMax7);
        AddLine(lines, ref count, StatType8, StatValue8, StatMax8);
        AddLine(lines, ref count, StatType9, StatValue9, StatMax9);
        AddLine(lines, ref count, StatType10, StatValue10, StatMax10);
        return new ItemRolls(Rarity, level, lines[..count]);
    }

    private static void AddLine(Span<ItemModifierRoll> lines, ref int count, StatType? type, uint? value, uint? max)
    {
        if (type is not { } stat || value is not { } low)
            return;
        if (count == lines.Length)
            throw new InvalidOperationException($"a unique carries at most {ItemRolls.MaxModifiers} stat lines");

        lines[count++] = new ItemModifierRoll(0, 0, stat, (int)low, (int)(max ?? low));
    }

    public uint MaxStackSize { get; set; }

    public uint DisplayId { get; set; }

    /// <summary>Common for a base, Unique for a hand-made item (#1179); Magic and Rare exist only on an instance's rolls.</summary>
    public ItemRarity Rarity { get; set; }

    /// <summary>The kind of base this template is (#1179), or None for an item that is no base.</summary>
    public ItemBaseKind BaseKind { get; set; }

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

    /// <summary>
    /// The high end of a range stat's line (#1179: the added-damage keys, <see cref="StatTypes.IsRange" />), whose
    /// <see cref="StatValue1" /> is the low end; null on every other line. The same for each StatMaxN.
    /// </summary>
    public uint? StatMax1 { get; set; }

    public StatType? StatType2 { get; set; }

    public uint? StatValue2 { get; set; }

    public uint? StatMax2 { get; set; }

    public StatType? StatType3 { get; set; }

    public uint? StatValue3 { get; set; }

    public uint? StatMax3 { get; set; }

    public StatType? StatType4 { get; set; }

    public uint? StatValue4 { get; set; }

    public uint? StatMax4 { get; set; }

    public StatType? StatType5 { get; set; }

    public uint? StatValue5 { get; set; }

    public uint? StatMax5 { get; set; }

    public StatType? StatType6 { get; set; }

    public uint? StatValue6 { get; set; }

    public uint? StatMax6 { get; set; }

    public StatType? StatType7 { get; set; }

    public uint? StatValue7 { get; set; }

    public uint? StatMax7 { get; set; }

    public StatType? StatType8 { get; set; }

    public uint? StatValue8 { get; set; }

    public uint? StatMax8 { get; set; }

    public StatType? StatType9 { get; set; }

    public uint? StatValue9 { get; set; }

    public uint? StatMax9 { get; set; }

    public StatType? StatType10 { get; set; }

    public uint? StatValue10 { get; set; }

    public uint? StatMax10 { get; set; }

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
    // Junk Subclasses
    JunkItem = 500
}
