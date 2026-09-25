using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>
/// One line of a <see cref="LootTable"/>. It drops an item, or rolls a whole other table: exactly
/// one of <see cref="ItemTemplateId"/> and <see cref="ReferenceTableId"/> is set, which a check
/// constraint and load-time validation both enforce.
/// </summary>
/// <remarks>
/// An entry with no <see cref="GroupId"/> rolls on its own against <see cref="Chance"/>. Entries
/// that share a <see cref="GroupId"/> form a group, which always drops exactly one of them, picked
/// with each entry's <see cref="Chance"/> used as its weight.
/// </remarks>
public class LootTableEntry
{
    public LootTableId LootTableId { get; set; } = default!;

    /// <summary>Order within the table. Unique per table; gaps are allowed. Rolls run in this order.</summary>
    public int Sequence { get; set; }

    public ItemTemplateId? ItemTemplateId { get; set; }

    public LootTableId? ReferenceTableId { get; set; }

    /// <summary>A percentage, 0-100. For a grouped entry it is the entry's weight within its group.</summary>
    public float Chance { get; set; }

    public int? GroupId { get; set; }

    /// <summary>At least 1. Ignored for a reference entry, which rolls its table once.</summary>
    public int MinCount { get; set; }

    public int MaxCount { get; set; }
}
