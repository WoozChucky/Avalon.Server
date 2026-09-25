using Avalon.World.Loot;

namespace Avalon.Server.World.UnitTests.Loot;

/// <summary>
/// Hands out exactly the numbers a test lists, in order, and fails loudly when the roller asks for
/// more than the test expected: an unexpected draw is a behaviour change worth seeing.
/// </summary>
internal sealed class ScriptedLootRandom : ILootRandom
{
    private readonly Queue<double> _doubles;
    private readonly Queue<long> _integers;

    public ScriptedLootRandom(double[] doubles, long[]? integers = null)
    {
        _doubles = new Queue<double>(doubles);
        _integers = new Queue<long>(integers ?? []);
    }

    public double NextDouble() =>
        _doubles.TryDequeue(out double next) ? next : throw new InvalidOperationException("no scripted double left");

    /// <summary>The range of the most recent NextInt64 request.</summary>
    public (long Min, long MaxExclusive)? LastIntegerRange { get; private set; }

    public long NextInt64(long minInclusive, long maxExclusive)
    {
        LastIntegerRange = (minInclusive, maxExclusive);
        if (!_integers.TryDequeue(out long next))
            throw new InvalidOperationException($"no scripted integer left for [{minInclusive}, {maxExclusive})");
        if (next < minInclusive || next >= maxExclusive)
            throw new InvalidOperationException($"scripted {next} is outside [{minInclusive}, {maxExclusive})");
        return next;
    }
}
