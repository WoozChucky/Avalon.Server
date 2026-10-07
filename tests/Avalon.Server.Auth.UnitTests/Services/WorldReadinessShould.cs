using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using NSubstitute;

namespace Avalon.Server.Auth.UnitTests.Services;

public sealed class WorldReadinessShould
{
    [Fact]
    public async Task Expired_or_unreadable_heartbeat_means_offline()
    {
        var cache = Substitute.For<IReplicatedCache>();
        var readiness = new WorldReadiness(cache);

        Assert.False(await readiness.IsReadyAsync(1, CancellationToken.None));
        cache.GetAsync(CacheKeys.WorldReady(1)).Returns<Task<string?>>(_ =>
            throw new InvalidOperationException("Redis unavailable"));
        Assert.False(await readiness.IsReadyAsync(1, CancellationToken.None));
    }

    [Fact]
    public void Maintenance_takes_precedence_over_a_live_heartbeat()
    {
        DateTime now = DateTime.UtcNow;
        Assert.Equal(WorldStatus.Maintenance,
            WorldReadiness.Resolve(new WorldMaintenanceState(true, 1, now), true, now));
        Assert.Equal(WorldStatus.Online,
            WorldReadiness.Resolve(new WorldMaintenanceState(false, 0, null), true, now));
        Assert.Equal(WorldStatus.Offline,
            WorldReadiness.Resolve(new WorldMaintenanceState(false, 0, null), false, now));
    }

    [Fact]
    public void Scheduled_world_stays_online_until_its_deadline()
    {
        DateTime now = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        var scheduled = new WorldMaintenanceState(true, 1, now.AddMinutes(10));

        Assert.Equal(WorldStatus.Online, WorldReadiness.Resolve(scheduled, true, now));
        Assert.Equal(WorldStatus.Offline, WorldReadiness.Resolve(scheduled, false, now));
        Assert.Equal(WorldStatus.Maintenance, WorldReadiness.Resolve(scheduled, true, now.AddMinutes(10)));
        Assert.Equal(WorldStatus.Maintenance,
            WorldReadiness.Resolve(new WorldMaintenanceState(true, 2, null), true, now));
    }
}
