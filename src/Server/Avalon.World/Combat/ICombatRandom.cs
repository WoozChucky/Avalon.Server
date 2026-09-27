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
    /// <summary>
    /// What a combat service built without a random uses (tests): no chance below 100 % ever procs, and
    /// a weapon rolls its low end, so a hit deals its base. Production registers <see cref="CombatRandom" />
    /// over <see cref="Random.Shared" />; WorldHostGraphShould pins it.
    /// </summary>
    public static readonly ICombatRandom Steady = new SteadyRandom();

    public double NextDouble() => random.NextDouble();

    public long NextInt64(long minInclusive, long maxInclusive) =>
        maxInclusive <= minInclusive ? minInclusive : random.NextInt64(minInclusive, maxInclusive + 1);

    private sealed class SteadyRandom : ICombatRandom
    {
        public double NextDouble() => 1d - 1e-9;

        public long NextInt64(long minInclusive, long maxInclusive) => minInclusive;
    }
}
