using Avalon.Api.Testing;
using Avalon.Configuration;
using Avalon.Infrastructure;
using Avalon.Infrastructure.WorldPlaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.WorldPlaces;

/// <summary>The place and queue scenarios (#967) against the in-memory store, as CI runs them.</summary>
public sealed class MemoryWorldPlaceStoreShould
{
    [Theory]
    [MemberData(nameof(WorldPlaceScenarios.Names), MemberType = typeof(WorldPlaceScenarios))]
    public Task Keep_the_place_and_queue_rules(string scenario) =>
        WorldPlaceScenarios.RunAsync(scenario, (clock, config) => new MemoryWorldPlaceStore(clock, config), new PlaceIds(1, 1));
}

/// <summary>
/// The same scenarios against the Redis scripts, on a throwaway Redis named by <c>AVALON_TEST_REDIS</c> (host:port). CI
/// has none and reports these skipped; the PR carries the result of a run (docs/world-player-limit.md). Each run takes
/// fresh world and account ids, so it needs an empty Redis no more than it changes one.
/// </summary>
public sealed class RedisWorldPlaceStoreShould
{
    [RedisTheory]
    [MemberData(nameof(WorldPlaceScenarios.Names), MemberType = typeof(WorldPlaceScenarios))]
    public async Task Keep_the_place_and_queue_rules(string scenario)
    {
        await using ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(RedisTheoryAttribute.Configuration!);
        IReplicatedCache cache = Cache(redis);
        PlaceIds ids = FreshIds();
        await WorldPlaceScenarios.RunAsync(scenario, (clock, config) => new RedisWorldPlaceStore(cache, Options.Create(config),
            Workloads(ids), clock), ids);
    }

    /// <summary>What the world lists read: places and waiters still current, nothing yet unswept.</summary>
    [RedisTheory]
    [InlineData(0)]
    public async Task Count_for_the_world_list_only_what_is_current(int unused)
    {
        _ = unused;
        await using ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(RedisTheoryAttribute.Configuration!);
        IReplicatedCache cache = Cache(redis);
        PlaceIds ids = FreshIds();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
        var store = new RedisWorldPlaceStore(cache, Options.Create(new WorldPlacesConfiguration()), Workloads(ids), clock);
        DateTime until = clock.GetUtcNow().UtcDateTime.AddSeconds(45);
        for (int account = 1; account <= 3; account++)
            await store.EnterAsync(new PlaceRequest(ids.World(1), ids.Account(account), until, 1, false, 5000), default);

        var occupancy = new RedisWorldOccupancy(cache, clock, NullLogger<RedisWorldOccupancy>.Instance);
        Assert.Equal(new WorldOccupancy(1, 2), await occupancy.ReadAsync(ids.World(1), default));
        clock.Advance(TimeSpan.FromSeconds(46)); // the place and both waiters are past their time, not yet swept
        Assert.Equal(new WorldOccupancy(0, 0), await occupancy.ReadAsync(ids.World(1), default));
    }

    private static IReplicatedCache Cache(ConnectionMultiplexer redis)
    {
        IReplicatedCache cache = Substitute.For<IReplicatedCache>();
        cache.Database.Returns(redis.GetDatabase());
        return cache;
    }

    private static PlaceIds FreshIds() =>
        new((ushort)Random.Shared.Next(10_000, 60_000), Random.Shared.NextInt64(1_000_000_000, 9_000_000_000));

    private static IOptions<GameWorkloadConfiguration> Workloads(PlaceIds ids) => Options.Create(new GameWorkloadConfiguration
    {
        Servers = [new GameServerDefinition { WorldId = ids.World(1) }, new GameServerDefinition { WorldId = ids.World(2) }],
    });
}

/// <summary>A theory that runs only where <c>AVALON_TEST_REDIS</c> names a throwaway Redis; skipped elsewhere, CI included.</summary>
public sealed class RedisTheoryAttribute : TheoryAttribute
{
    public const string Variable = "AVALON_TEST_REDIS";

    public static string? Configuration => Environment.GetEnvironmentVariable(Variable);

    public RedisTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Configuration))
            Skip = $"Runs against a throwaway Redis only: set {Variable} to its host:port (docs/world-player-limit.md).";
    }
}
