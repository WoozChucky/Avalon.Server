using Avalon.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Avalon.Server.World.Presence;

public sealed class WorldReadyHeartbeatService(
    ushort worldId,
    Func<bool> isListening,
    Func<long> completedTicks,
    IReplicatedCache cache,
    ILogger<WorldReadyHeartbeatService> logger) : BackgroundService
{
    private static readonly TimeSpan s_interval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan s_ttl = TimeSpan.FromSeconds(5);
    private long _lastPublishedTick = -1;

    public async Task PublishOnceAsync(CancellationToken ct)
    {
        long ticks = completedTicks();
        if (!isListening() || ticks <= 0 || ticks == _lastPublishedTick)
            return;

        if (await cache.SetAsync(CacheKeys.WorldReady(worldId), "1", s_ttl))
            _lastPublishedTick = ticks;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(s_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await PublishOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "World {WorldId} ready heartbeat failed; retrying", worldId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        try
        {
            await cache.RemoveAsync(CacheKeys.WorldReady(worldId));
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "World {WorldId} ready heartbeat removal failed", worldId);
        }
    }
}
