using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Avalon.Infrastructure.WorldPlaces;

public readonly record struct WorldOccupancy(int PlayerCount, int QueueLength);

/// <summary>A world's places taken and line length, for the world lists: two reads, no write (#967).</summary>
public interface IWorldOccupancy
{
    /// <summary>The counts now; zero for both when Redis cannot be read, as <see cref="WorldReadiness"/> reads a world as not ready.</summary>
    Task<WorldOccupancy> ReadAsync(ushort worldId, CancellationToken cancellationToken);
}

/// <summary>
/// Counts only entries whose expiry or check-in deadline is still ahead, so what no script has swept yet is not shown.
/// Needs <c>ZCOUNT</c> on <c>world:*:places</c> and <c>world:*:queue:checkins</c> and nothing else: the worlds
/// service's Redis user reads them. An error Redis answers (a missing permission, <c>NOPERM</c>, above all) still reads
/// as 0, but is logged at Warning, at most once per <see cref="PlaceLedgerOutage.Interval"/>, so a user without the
/// rules does not show every world empty in silence.
/// </summary>
public sealed class RedisWorldOccupancy(IReplicatedCache cache, TimeProvider clock, ILogger<RedisWorldOccupancy> logger)
    : IWorldOccupancy
{
    private readonly LogThrottle _throttle = new(clock, PlaceLedgerOutage.Interval);

    public async Task<WorldOccupancy> ReadAsync(ushort worldId, CancellationToken cancellationToken)
    {
        try
        {
            double now = clock.GetUtcNow().ToUnixTimeMilliseconds();
            IDatabase database = cache.Database;
            Task<long> places = database.SortedSetLengthAsync(CacheKeys.WorldPlaces(worldId), now, double.PositiveInfinity, Exclude.Start);
            Task<long> waiting = database.SortedSetLengthAsync(CacheKeys.WorldQueueCheckIns(worldId), now, double.PositiveInfinity, Exclude.Start);
            await Task.WhenAll(places, waiting).WaitAsync(cancellationToken);
            return new WorldOccupancy((int)places.Result, (int)waiting.Result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RedisServerException error)
        {
            if (_throttle.TryEnter(out int suppressed))
            {
                logger.LogWarning("The world list could not read world {WorldId}'s places from Redis ({Error}); it shows 0 until the read succeeds. Check the Redis user's rules (docs/redis-cache-keys.md). {Suppressed} earlier failures were not logged",
                    worldId, error.Message, suppressed);
            }

            return default;
        }
        catch (Exception error) when (error is RedisException or TimeoutException)
        {
            return default;
        }
    }
}
