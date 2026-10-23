namespace Avalon.Combat;

/// <summary>
/// A random pinned to one end of every range (#669, #1178), so an amount's lowest and highest come from the very
/// arithmetic a hit uses. Only ranges are ever asked of it; a chance roll answers 0.
/// </summary>
public sealed class RollEnds : ICombatRandom
{
    public static readonly ICombatRandom Low = new RollEnds(false);
    public static readonly ICombatRandom High = new RollEnds(true);

    private readonly bool _high;

    private RollEnds(bool high) => _high = high;

    public double NextDouble() => 0d;

    public long NextInt64(long minInclusive, long maxInclusive) =>
        _high ? Math.Max(minInclusive, maxInclusive) : minInclusive;
}
