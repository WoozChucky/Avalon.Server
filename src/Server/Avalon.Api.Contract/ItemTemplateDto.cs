using Avalon.Common;

namespace Avalon.Api.Contract;

public sealed class ItemTemplateDto
{
    public ulong Id { get; set; }
    public string Name { get; set; } = "";
    public ItemClass Class { get; set; }
    public ItemSubClass SubClass { get; set; }
    public ItemTemplateFlags Flags { get; set; }
    public bool Stackable { get; set; }
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
    /// <summary>Milliseconds between a weapon's basic attacks before attack speed (#1178), above 0; null for an item that is not a weapon.</summary>
    public uint? AttackTimeMs { get; set; }
    public uint? DamageMin1 { get; set; }
    public uint? DamageMax1 { get; set; }
    public DamageType? DamageType1 { get; set; }
    public uint? DamageMin2 { get; set; }
    public uint? DamageMax2 { get; set; }
    public DamageType? DamageType2 { get; set; }
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

    /// <summary>
    /// The row's version: a lowercase hex SHA-256 of its stored values, also sent as the ETag on a single read.
    /// An edit sends it back as If-Match.
    /// </summary>
    public string Version { get; set; } = "";

    /// <summary>Whether this world's templates can be edited; false on a read-only world.</summary>
    public bool Editable { get; set; }
}
