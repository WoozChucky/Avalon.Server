using Avalon.Common;

namespace Avalon.Api.Contract;

/// <summary>The body of a template edit: the template read shape without its id, version and computed fields.</summary>
public sealed class UpdateItemTemplateRequest
{
    public string Name { get; set; } = "";
    public ItemClass Class { get; set; }
    public ItemSubClass SubClass { get; set; }
    public ItemTemplateFlags Flags { get; set; }
    public uint MaxStackSize { get; set; }
    public uint DisplayId { get; set; }
    public ItemRarity Rarity { get; set; }
    public uint BuyPrice { get; set; }
    public uint SellPrice { get; set; }
    public ItemSlotType? Slot { get; set; }
    /// <summary>What kind of base the item is (#1179), which needs a <see cref="Slot"/>; null for a consumable or quest item.</summary>
    public ItemBaseKind? BaseKind { get; set; }
    /// <summary>The classes that may equip the item; empty when every class may (#1171).</summary>
    public List<CharacterClass> AllowedClasses { get; set; } = [];
    public ushort? RequiredLevel { get; set; }
    /// <summary>The Strength a character needs to equip the item, or null for none (#1178).</summary>
    public ushort? RequiredStrength { get; set; }
    /// <summary>The Agility a character needs to equip the item, or null for none (#1178).</summary>
    public ushort? RequiredAgility { get; set; }
    /// <summary>The Intellect a character needs to equip the item, or null for none (#1178).</summary>
    public ushort? RequiredIntellect { get; set; }
    /// <summary>
    /// Whether any one of the set attribute requirements is enough (#1179, a mace's Strength or Intellect); false when
    /// every set one is needed.
    /// </summary>
    public bool RequiresAnyAttribute { get; set; }
    /// <summary>The type of a weapon's damage (#1178), set on a weapon kind and on no other base (#1179).</summary>
    public DamageType? DamageType { get; set; }
    /// <summary>
    /// The base a unique is made on (#1179): a Common template of its kind at or under its required level, whose base
    /// stats and implicit every copy rolls. Set on every Unique template and on no other.
    /// </summary>
    public ulong? BaseTemplateId { get; set; }
    /// <summary>
    /// The base-stat ranges every copy rolls within (#1179), by stat: exactly the stats its kind carries (armour, evasion
    /// or ward on armour, armour and block on a shield, ward on a caster off-hand, damage and attack time on a weapon),
    /// none on jewellery, a trinket, a unique or an item that is no base. An edit replaces the whole list, as it does
    /// AllowedClasses; it applies to copies made afterwards, never to a copy already made.
    /// </summary>
    public List<ItemTemplateBaseStatDto> BaseStats { get; set; } = [];
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
    public string? UseScript { get; set; }
    public uint? UseCastTimeMs { get; set; }
    public uint? UseCooldownMs { get; set; }
    public string? UseCooldownGroup { get; set; }
    public uint? UseValue { get; set; }
}
