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

    /// <summary>The item's modifiers as they were when it moved: the record's own copy, since it outlives the item.</summary>
    public List<TradeRecordItemModifier> Modifiers { get; set; } = [];
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
    public ushort ModifierId { get; set; }
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
