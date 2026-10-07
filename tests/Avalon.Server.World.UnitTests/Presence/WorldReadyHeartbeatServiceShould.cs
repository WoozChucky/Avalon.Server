using Avalon.Infrastructure;
using Avalon.Server.World.Presence;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Presence;

public sealed class WorldReadyHeartbeatServiceShould
{
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();
    private bool _listening;
    private long _ticks;

    public WorldReadyHeartbeatServiceShould()
    {
        _cache.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>()).Returns(true);
    }

    private WorldReadyHeartbeatService Service() => new(
        1, () => _listening, () => _ticks, _cache,
        NullLogger<WorldReadyHeartbeatService>.Instance);

    [Fact]
    public async Task Publish_only_after_listener_and_tick_are_ready()
    {
        WorldReadyHeartbeatService service = Service();
        await service.PublishOnceAsync(CancellationToken.None);
        _ticks = 1;
        await service.PublishOnceAsync(CancellationToken.None);
        await _cache.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>());

        _listening = true;
        await service.PublishOnceAsync(CancellationToken.None);

        await _cache.Received(1).SetAsync(CacheKeys.WorldReady(1), "1", TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Stop_renewing_when_tick_stalls_and_remove_on_stop()
    {
        _listening = true;
        _ticks = 1;
        WorldReadyHeartbeatService service = Service();
        await service.PublishOnceAsync(CancellationToken.None);
        await service.PublishOnceAsync(CancellationToken.None);
        await _cache.Received(1).SetAsync(CacheKeys.WorldReady(1), "1", TimeSpan.FromSeconds(5));

        _ticks = 2;
        await service.PublishOnceAsync(CancellationToken.None);
        await _cache.Received(2).SetAsync(CacheKeys.WorldReady(1), "1", TimeSpan.FromSeconds(5));

        await service.StopAsync(CancellationToken.None);
        await _cache.Received(1).RemoveAsync(CacheKeys.WorldReady(1));
    }
}
