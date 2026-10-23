using Avalon.Common;
using Avalon.Common.ValueObjects;

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

    /// <summary>The rolls the item carried when it moved (#1179), or null for an item that has none.</summary>
    public ItemRolls? Rolls { get; set; }

    /// <summary>The instance flags the item carried when it moved (#1179).</summary>
    public ItemInstanceFlags Flags { get; set; }
}
