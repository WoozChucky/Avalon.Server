using System.Text.Json.Serialization;
using Avalon.Common;

namespace Avalon.Api.Contract;

/// <summary>An item as a tooltip shows it to anyone: no raw flags, buy price or display id.</summary>
public sealed class PublicItemDto
{
    public ulong Id { get; set; }
    public string Name { get; set; } = "";
    public ItemRarity Rarity { get; set; }
    public ItemClass Class { get; set; }
    public ItemSubClass SubClass { get; set; }
    public ItemSlotType? Slot { get; set; }
    public ushort? RequiredLevel { get; set; }
    /// <summary>The Strength a character needs to equip the item, or null for none (#1178).</summary>
    public ushort? RequiredStrength { get; set; }
    /// <summary>The Agility a character needs to equip the item, or null for none (#1178).</summary>
    public ushort? RequiredAgility { get; set; }
    /// <summary>The Intellect a character needs to equip the item, or null for none (#1178).</summary>
    public ushort? RequiredIntellect { get; set; }
    /// <summary>Whether any one of the set attribute requirements is enough (#1179, a mace's Strength or Intellect).</summary>
    public bool RequiresAnyAttribute { get; set; }
    /// <summary>Milliseconds between a weapon's basic attacks before attack speed (#1178), above 0; null for an item that is not a weapon.</summary>
    public uint? AttackTimeMs { get; set; }
    /// <summary>Empty when every class may use it.</summary>
    public List<CharacterClass> AllowedClasses { get; set; } = [];
    public uint MaxStackSize { get; set; }
    /// <summary>Gold.</summary>
    public uint SellPrice { get; set; }
    public bool CannotBeSold { get; set; }
    public ItemAttunement Attunes { get; set; }
    public ItemUniqueness Unique { get; set; }
    public List<PublicItemDamageDto> Damage { get; set; } = [];
    /// <summary>The template's stat pairs that are set, in slot order. Armor is one of them.</summary>
    public List<PublicItemStatDto> Stats { get; set; } = [];
    /// <summary>
    /// The lines every copy rolls within (#1179): a base's implicits or a unique's own modifier, each line a range, in
    /// the order the item shows them; empty for an item that links none.
    /// </summary>
    public List<PublicItemSpanDto> ModifierSpans { get; set; } = [];
}

/// <summary>
/// One stat line a copy of the item rolls within (#1179): <see cref="Min" /> to <see cref="Max" />, and on an "adds X to
/// Y" line its high end's range, <see cref="Min2" /> to <see cref="Max2" />.
/// </summary>
public sealed class PublicItemSpanDto
{
    public ItemModifierKind Kind { get; set; }
    public StatType Stat { get; set; }
    public int Min { get; set; }
    public int Max { get; set; }
    public int? Min2 { get; set; }
    public int? Max2 { get; set; }
}

public sealed class PublicItemDamageDto
{
    public uint Min { get; set; }
    public uint Max { get; set; }
    public DamageType? Type { get; set; }
}

public sealed class PublicItemStatDto
{
    public StatType Type { get; set; }
    public uint Value { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ItemAttunement { None, OnPickup, OnEquip, OnUse, ToAccount }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ItemUniqueness { None, Unique, UniqueEquipped }
