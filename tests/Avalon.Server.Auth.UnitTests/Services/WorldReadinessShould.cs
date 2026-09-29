using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using NSubstitute;
using Xunit;

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
        Assert.Equal(WorldStatus.Maintenance,
            WorldReadiness.Resolve(new WorldMaintenanceState(true, 1, DateTime.UtcNow), true));
        Assert.Equal(WorldStatus.Online,
            WorldReadiness.Resolve(new WorldMaintenanceState(false, 0, null), true));
        Assert.Equal(WorldStatus.Offline,
            WorldReadiness.Resolve(new WorldMaintenanceState(false, 0, null), false));
    }
}
