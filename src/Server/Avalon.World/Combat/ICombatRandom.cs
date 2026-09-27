namespace Avalon.World.Combat;

/// <summary>
/// Where every combat roll gets its numbers (#506): dodge, crit and block chances, weapon rolls and
/// creature swings, so a test can pin the outcome. No other random source is used in combat.
/// </summary>
public interface ICombatRandom
{
    /// <summary>A number in [0, 1).</summary>
    double NextDouble();

    /// <summary>A number in [<paramref name="minInclusive" />, <paramref name="maxInclusive" />], both ends included.</summary>
    long NextInt64(long minInclusive, long maxInclusive);
}

/// <summary>Production randomness. Registered over <see cref="Random.Shared" />, which is thread-safe.</summary>
public sealed class CombatRandom(Random random) : ICombatRandom
{
    public double NextDouble() => random.NextDouble();

    public long NextInt64(long minInclusive, long maxInclusive) =>
        maxInclusive <= minInclusive ? minInclusive : random.NextInt64(minInclusive, maxInclusive + 1);
}
