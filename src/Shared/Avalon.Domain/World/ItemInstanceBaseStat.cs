using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>
/// One base stat a copy rolled when it was made (#1179), within its template's range at the time, keyed by
/// (ItemInstanceId, Stat). The relational record of a copy's base stats; in memory they travel as one value,
/// <see cref="ItemBaseStats" />, built by <see cref="ItemBaseStats.Of" /> and written back from its enumerator.
/// </summary>
public class ItemInstanceBaseStat
{
    public ItemInstanceId ItemInstanceId { get; set; } = null!;

    public ItemBaseStat Stat { get; set; }

    public int Value { get; set; }
}
