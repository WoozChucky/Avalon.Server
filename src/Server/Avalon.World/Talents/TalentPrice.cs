using Avalon.World.Configuration;

namespace Avalon.World.Talents;

/// <summary>
/// What a talent refund at the Lectern of Paths costs (#1181 Part C), from <c>Game:Talents</c>: free up to and including
/// <see cref="TalentConfiguration.FreeRefundMaxLevel" />, then per node the base price plus the per-level price for each
/// level past the first paying one. The price is the character's level's when it refunds; nothing is stored.
/// </summary>
public static class TalentPrice
{
    /// <summary>
    /// The gold one node costs at <paramref name="level" />: 0 up to the free level, otherwise
    /// <c>RefundBasePrice + RefundPricePerLevel × (level − FreeRefundMaxLevel − 1)</c>, saturating at
    /// <see cref="ulong.MaxValue" />.
    /// </summary>
    public static ulong PerNode(ushort level, TalentConfiguration config)
    {
        if (level <= config.FreeRefundMaxLevel)
            return 0;

        UInt128 price = config.RefundBasePrice
            + (UInt128)config.RefundPricePerLevel * (uint)(level - config.FreeRefundMaxLevel - 1);
        return price > ulong.MaxValue ? ulong.MaxValue : (ulong)price;
    }

    /// <summary>
    /// <paramref name="perNode" /> times <paramref name="nodes" />, saturating at <see cref="ulong.MaxValue" />: a total
    /// no wallet holds, so the refund is refused for its gold.
    /// </summary>
    public static ulong Total(ulong perNode, int nodes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nodes);
        UInt128 total = (UInt128)perNode * (uint)nodes;
        return total > ulong.MaxValue ? ulong.MaxValue : (ulong)total;
    }
}
