using System.Collections.Concurrent;
using Avalon.Infrastructure;
using NSubstitute;

namespace Avalon.Server.Auth.UnitTests.Services;

/// <summary>
/// A cache whose counters behave like the Redis ones the budgets use: an atomic increment and a
/// decrement floored at zero, per key. Everything else is a plain substitute.
/// </summary>
internal sealed class CounterCache
{
    private readonly ConcurrentDictionary<string, long> _counts = new();

    public IReplicatedCache Cache { get; } = Substitute.For<IReplicatedCache>();

    public CounterCache()
    {
        Cache.IncrementAsync(Arg.Any<string>(), Arg.Any<TimeSpan>())
            .Returns(ci => _counts.AddOrUpdate(ci.ArgAt<string>(0), 1, (_, v) => v + 1));
        Cache.DecrementFloorAsync(Arg.Any<string>())
            .Returns(ci => _counts.AddOrUpdate(ci.ArgAt<string>(0), 0, (_, v) => Math.Max(0, v - 1)));
    }

    public long CountOf(string key) => _counts.GetValueOrDefault(key);

    /// <summary>The keys of the per-username budget this cache has seen.</summary>
    public IReadOnlyList<string> UsernameKeys =>
        _counts.Keys.Where(k => k.StartsWith("auth:username:", StringComparison.Ordinal)).ToList();
}
