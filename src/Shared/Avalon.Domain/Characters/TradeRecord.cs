using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;

namespace Avalon.Domain.Characters;

/// <summary>
/// One completed trade between two characters (#1175), written in the same transaction as the paired save that moved
/// its items and gold, so it exists exactly when that save committed. It is the permanent trade history, and it settles
/// a save whose outcome is unknown. Permanent: no foreign key to the characters, the items or the templates, so it
/// outlives a deleted character, an item used up and a template a reload removed.
/// </summary>
public class TradeRecord
{
    /// <summary>The trade's id, from <see cref="Guid.CreateVersion7()" />; the database never generates one.</summary>
    public Guid Id { get; set; }

    public CharacterId FirstCharacterId { get; set; } = default!;
    public CharacterId SecondCharacterId { get; set; } = default!;

    /// <summary>The gold <see cref="FirstCharacterId" /> gave.</summary>
    public ulong FirstGold { get; set; }

    /// <summary>The gold <see cref="SecondCharacterId" /> gave.</summary>
    public ulong SecondGold { get; set; }

    /// <summary>When the trade completed, in UTC.</summary>
    public DateTime CompletedAt { get; set; }

    /// <summary>Every item either side gave.</summary>
    public List<TradeRecordItem> Items { get; set; } = [];
}

/// <summary>
/// One item a trade moved: who gave it, what it was and how many. The receiver is the record's other character.
/// Keyed by the trade and the item instance, and indexed by the instance, so where an item came from can be found.
/// </summary>
public class TradeRecordItem
{
    public Guid TradeId { get; set; }
    public ItemInstanceId ItemInstanceId { get; set; } = default!;
    public CharacterId GiverCharacterId { get; set; } = default!;
    public ItemTemplateId TemplateId { get; set; } = default!;
    public uint Count { get; set; }
    public ItemRarity Rarity { get; set; }
    public ushort ItemLevel { get; set; }

    /// <summary>A rare item's name as it was when it moved (#1179); null for every other item.</summary>
    public string? RareName { get; set; }

    /// <summary>Whether the item was identified when it moved (#1179); a common item always was.</summary>
    public required bool Identified { get; set; }

    /// <summary>
    /// The item's hidden instability when it moved (#1180, <see cref="ItemInstance.Instability" />): kept so the record is
    /// the whole copy, and never shown.
    /// </summary>
    public ushort Instability { get; set; }

    /// <summary>Whether the item was Tainted when it moved (#1180): exactly when its instability had reached the brick.</summary>
    public bool Tainted { get; set; }

    /// <summary>The item's base stats as they were when it moved (#1179): the record's own copy, one row per stat.</summary>
    public List<TradeRecordItemBaseStat> BaseStats { get; set; } = [];

    /// <summary>The item's modifiers as they were when it moved: the record's own copy, since it outlives the item.</summary>
    public List<TradeRecordItemModifier> Modifiers { get; set; } = [];
}

/// <summary>One base stat of a traded item, as it was when the trade completed (#1179), keyed by the trade, the item and the stat.</summary>
public class TradeRecordItemBaseStat
{
    public Guid TradeId { get; set; }
    public ItemInstanceId ItemInstanceId { get; set; } = default!;
    public ItemBaseStat Stat { get; set; }
    public int Value { get; set; }
}

/// <summary>
/// One modifier of a traded item, as it was when the trade completed (#1179). Keyed by the trade and the modifier's own
/// id (<see cref="ItemInstanceModifier.Id" />), which stays the modifier's whatever its place on the item becomes.
/// </summary>
public class TradeRecordItemModifier
{
    public Guid TradeId { get; set; }

    /// <summary>The id of the item's modifier row (<see cref="ItemInstanceModifier.Id" />).</summary>
    public Guid ItemInstanceModifierId { get; set; }

    public ItemInstanceId ItemInstanceId { get; set; } = default!;
    public byte Ordinal { get; set; }

    /// <summary>The catalog modifier (<see cref="ItemInstanceModifier.ModifierId" />).</summary>
    public int ModifierId { get; set; }

    /// <summary>A sealed affix's progress when the trade completed (<see cref="ItemInstanceModifier.SealProgress" />, #1180); null on an awake one.</summary>
    public int? SealProgress { get; set; }

    /// <summary>Whether the modifier came from a glyph (<see cref="ItemInstanceModifier.Imprinted" />, #1180).</summary>
    public bool Imprinted { get; set; }

    public List<TradeRecordItemModifierValue> Values { get; set; } = [];
}

/// <summary>One rolled number of a traded item's modifier, as it was when the trade completed (#1179).</summary>
public class TradeRecordItemModifierValue
{
    public Guid TradeId { get; set; }
    public Guid ItemInstanceModifierId { get; set; }
    public byte Index { get; set; }
    public StatType Stat { get; set; }
    public int Value { get; set; }
    public int? Value2 { get; set; }
}
