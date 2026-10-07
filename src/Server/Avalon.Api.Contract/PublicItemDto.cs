using System.Text.Json.Serialization;
using Avalon.World.Public.Enums;

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
    public ushort? ItemPower { get; set; }
    /// <summary>Empty when every class may use it.</summary>
    public List<CharacterClass> AllowedClasses { get; set; } = [];
    public uint MaxStackSize { get; set; }
    /// <summary>In copper.</summary>
    public uint SellPrice { get; set; }
    public bool CannotBeSold { get; set; }
    public ItemAttunement Attunes { get; set; }
    public ItemUniqueness Unique { get; set; }
    public List<PublicItemDamageDto> Damage { get; set; } = [];
    /// <summary>The template's stat pairs that are set, in slot order. Armor is one of them.</summary>
    public List<PublicItemStatDto> Stats { get; set; } = [];
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
