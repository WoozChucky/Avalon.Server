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
        WorldPlaceScenarios.RunAsync(scenario, (clock, config) => new MemoryWorldPlaceStore(clock, config, [1, 2]), new PlaceIds(1, 1));
}

/// <summary>
/// The same scenarios against the Redis scripts, on a throwaway Redis named by <c>AVALON_TEST_REDIS</c> (host:port). CI
/// has none and reports these skipped; the PR carries the result of a run (docs/world-player-limit.md). Every row uses
/// the same world and account ids and deletes their keys before it starts, so it never meets what an earlier row or run
/// left there: it needs no empty Redis, and the data it leaves behind is only ever its own ids' keys.
/// </summary>
public sealed class RedisWorldPlaceStoreShould
{
    /// <summary>The ids every row uses, far from any a local run of the services gives out.</summary>
    private static readonly PlaceIds s_ids = new(40_001, 7_000_000_001);

    [RedisTheory]
    [MemberData(nameof(WorldPlaceScenarios.Names), MemberType = typeof(WorldPlaceScenarios))]
    public async Task Keep_the_place_and_queue_rules(string scenario)
    {
        await using ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(RedisTheoryAttribute.Configuration!);
        IReplicatedCache cache = await CleanAsync(redis);
        await WorldPlaceScenarios.RunAsync(scenario, (clock, config) => new RedisWorldPlaceStore(cache, Options.Create(config),
            Workloads(s_ids), clock, new PlaceLedgerOutage(NullLogger<PlaceLedgerOutage>.Instance, clock)), s_ids);
    }

    /// <summary>What the world lists read: places and waiters still current, nothing yet unswept.</summary>
    [RedisTheory]
    [InlineData(0)]
    public async Task Count_for_the_world_list_only_what_is_current(int unused)
    {
        _ = unused;
        await using ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(RedisTheoryAttribute.Configuration!);
        IReplicatedCache cache = await CleanAsync(redis);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
        var store = new RedisWorldPlaceStore(cache, Options.Create(new WorldPlacesConfiguration()), Workloads(s_ids), clock,
            new PlaceLedgerOutage(NullLogger<PlaceLedgerOutage>.Instance, clock));
        DateTime until = clock.GetUtcNow().UtcDateTime.AddSeconds(45);
        for (int account = 1; account <= 3; account++)
            await store.EnterAsync(new PlaceRequest(s_ids.World(1), s_ids.Account(account), until, 1, false, 5000), default);

        var occupancy = new RedisWorldOccupancy(cache, clock, NullLogger<RedisWorldOccupancy>.Instance);
        Assert.Equal(new WorldOccupancy(1, 2), await occupancy.ReadAsync(s_ids.World(1), default));
        clock.Advance(TimeSpan.FromSeconds(46)); // the place and both waiters are past their time, not yet swept
        Assert.Equal(new WorldOccupancy(0, 0), await occupancy.ReadAsync(s_ids.World(1), default));
    }

    /// <summary>The cache over <paramref name="redis"/>, with every key of the rows' two worlds and sixteen accounts deleted.</summary>
    private static async Task<IReplicatedCache> CleanAsync(ConnectionMultiplexer redis)
    {
        IDatabase database = redis.GetDatabase();
        var keys = new List<RedisKey>();
        foreach (ushort world in new[] { s_ids.World(1), s_ids.World(2) })
        {
            keys.AddRange([
                CacheKeys.WorldPlaces(world), CacheKeys.WorldPlaceMarks(world), CacheKeys.WorldQueue(world),
                CacheKeys.WorldQueueCheckIns(world), CacheKeys.WorldQueueArrivals(world),
            ]);
        }

        for (int account = 1; account <= 16; account++) keys.Add(CacheKeys.WorldQueueWaiting(s_ids.Account(account)));
        await database.KeyDeleteAsync([.. keys]);
        IReplicatedCache cache = Substitute.For<IReplicatedCache>();
        cache.Database.Returns(database);
        return cache;
    }

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
