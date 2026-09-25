namespace Avalon.World.Loot;

/// <summary>Where loot rolls get their numbers, so a test can pin the outcome.</summary>
public interface ILootRandom
{
    /// <summary>A number in [0, 1).</summary>
    double NextDouble();

    /// <summary>A number in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
    long NextInt64(long minInclusive, long maxExclusive);
}

/// <summary>Production randomness. Registered over <see cref="Random.Shared"/>, which is thread-safe.</summary>
public sealed class LootRandom(Random random) : ILootRandom
{
    public double NextDouble() => random.NextDouble();

    public long NextInt64(long minInclusive, long maxExclusive) => random.NextInt64(minInclusive, maxExclusive);
}
