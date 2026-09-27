using Avalon.World.Combat;

namespace Avalon.Server.World.UnitTests.Combat;

/// <summary>
/// A combat random that answers from queues the test fills, and throws when asked for a number it was
/// not given, so a test also proves which rolls were drawn (#506). Empty, it proves none were.
/// </summary>
internal sealed class ScriptedCombatRandom : ICombatRandom
{
    private readonly Queue<double> _doubles = new();
    private readonly Queue<long> _longs = new();

    public ScriptedCombatRandom(params double[] doubles)
    {
        foreach (double d in doubles) _doubles.Enqueue(d);
    }

    /// <summary>Every (min, max) a weapon roll was drawn over, in order.</summary>
    public List<(long Min, long Max)> WeaponRolls { get; } = [];

    public int DoublesDrawn { get; private set; }

    public ScriptedCombatRandom Doubles(params double[] doubles)
    {
        foreach (double d in doubles) _doubles.Enqueue(d);
        return this;
    }

    public ScriptedCombatRandom Longs(params long[] longs)
    {
        foreach (long l in longs) _longs.Enqueue(l);
        return this;
    }

    public int DoublesLeft => _doubles.Count;

    public int LongsLeft => _longs.Count;

    public double NextDouble()
    {
        if (!_doubles.TryDequeue(out double value))
            throw new InvalidOperationException("a combat roll was drawn that the test did not script");
        DoublesDrawn++;
        return value;
    }

    public long NextInt64(long minInclusive, long maxInclusive)
    {
        WeaponRolls.Add((minInclusive, maxInclusive));
        if (!_longs.TryDequeue(out long value))
            throw new InvalidOperationException("a weapon roll was drawn that the test did not script");
        return value;
    }

    /// <summary>No hit lands a dodge, crit or block: every chance roll is 0.99.</summary>
    public static ScriptedCombatRandom Plain(int rolls = 300) => new(Enumerable.Repeat(0.99, rolls).ToArray());
}
