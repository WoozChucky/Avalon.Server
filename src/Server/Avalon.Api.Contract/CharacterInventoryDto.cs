namespace Avalon.Api.Contract;

public sealed class CharacterInventoryDto
{
    public uint CharacterId { get; set; }
    public List<CharacterInventoryItemDto> Items { get; set; } = new();
}

public sealed class CharacterInventoryItemDto
{
    public Guid ItemId { get; set; }
    public InventoryType Container { get; set; }
    public byte Slot { get; set; }
    public uint Count { get; set; }
    public uint Durability { get; set; }
    /// <summary>This copy's rarity (#1179): Magic and Rare copies roll on a Common template.</summary>
    public ItemRarity Rarity { get; set; }
    /// <summary>This copy's item level, at least 1 (#1179).</summary>
    public ushort ItemLevel { get; set; }
    /// <summary>The modifiers this copy rolled, in the order the item shows them; empty for none (#1179).</summary>
    public List<CharacterInventoryModifierDto> Modifiers { get; set; } = [];
    public CharacterInventoryItemTemplateDto? Template { get; set; }
}

/// <summary>One modifier an item copy carries (#1179).</summary>
public sealed class CharacterInventoryModifierDto
{
    /// <summary>The catalog modifier it rolled.</summary>
    public int ModifierId { get; set; }
    /// <summary>Its rolled numbers, in the order of the catalog's stat lines.</summary>
    public List<CharacterInventoryModifierValueDto> Values { get; set; } = [];
}

/// <summary>One rolled number of an item's modifier: the stat it gives and how much (#1179).</summary>
public sealed class CharacterInventoryModifierValueDto
{
    public StatType Stat { get; set; }
    public int Value { get; set; }
    /// <summary>The high end of an "adds X to Y" line; null on every other line.</summary>
    public int? Value2 { get; set; }
}

public sealed class CharacterInventoryItemTemplateDto
{
    public ulong Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public uint DisplayId { get; set; }
    /// <summary>The slot the item is worn in; null for an item that is not worn, such as a potion.</summary>
    public ItemSlotType? SlotType { get; set; }
    public ushort RequiredLevel { get; set; }
}
