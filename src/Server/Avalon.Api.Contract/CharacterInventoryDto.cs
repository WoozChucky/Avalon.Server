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
    /// <summary>
    /// The modifiers this copy rolled, in the order the item shows them; empty for none (#1179). An unidentified copy
    /// lists its implicit lines only.
    /// </summary>
    public List<CharacterInventoryModifierDto> Modifiers { get; set; } = [];
    /// <summary>
    /// This copy's name (#1179): an identified magic copy's prefix and suffix around its template's name, or an identified
    /// rare copy's two words; null where the template's name is the item's, and for every unidentified copy.
    /// </summary>
    public string? Name { get; set; }
    public CharacterInventoryItemTemplateDto? Template { get; set; }
    /// <summary>The base stats this copy rolled, by stat; empty for a copy that has none (#1179).</summary>
    public List<CharacterInventoryBaseStatDto> BaseStats { get; set; } = [];
    /// <summary>
    /// Whether this copy is identified (#1179). An unidentified copy (magic, rare or unique) shows its rarity, item level,
    /// base stats and implicit lines, and no other modifier and no name.
    /// </summary>
    public bool Identified { get; set; }
    /// <summary>
    /// Whether this copy is attuned (#1180): one of its affixes has woken, and it can no longer be traded.
    /// </summary>
    public bool Attuned { get; set; }
}

/// <summary>One base stat an item copy rolled (#1179): its own number, which its local modifiers scale.</summary>
public sealed class CharacterInventoryBaseStatDto
{
    public ItemBaseStat Stat { get; set; }
    public int Value { get; set; }
}

/// <summary>One modifier an item copy carries (#1179).</summary>
public sealed class CharacterInventoryModifierDto
{
    /// <summary>The catalog modifier it rolled.</summary>
    public int ModifierId { get; set; }
    /// <summary>Its rolled numbers, in the order of the catalog's stat lines.</summary>
    public List<CharacterInventoryModifierValueDto> Values { get; set; } = [];
    /// <summary>What the catalog modifier is, from the world's catalog; null when the catalog no longer holds it.</summary>
    public ItemModifierKind? Kind { get; set; }
    /// <summary>
    /// The Resonance theme of a prefix or suffix line, from the world's catalog (#1180); null on an implicit or unique line
    /// and when the catalog no longer holds the modifier.
    /// </summary>
    public int? ThemeId { get; set; }
    /// <summary>
    /// A sealed affix's points toward waking (#1180): set exactly on a sealed line the read shows, null on an awake one. A
    /// sealed affix adds nothing to the item until it wakes.
    /// </summary>
    public int? SealProgress { get; set; }
    /// <summary>
    /// The points a sealed affix wakes at, for the copy's item level (#1180): set exactly when <see cref="SealProgress" />
    /// is, null otherwise.
    /// </summary>
    public int? SealThreshold { get; set; }
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
