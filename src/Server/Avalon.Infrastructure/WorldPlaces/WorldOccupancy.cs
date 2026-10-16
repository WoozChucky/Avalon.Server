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
/// service's Redis user reads them.
/// </summary>
public sealed class RedisWorldOccupancy(IReplicatedCache cache, TimeProvider clock) : IWorldOccupancy
{
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
        catch (Exception error) when (error is RedisException or TimeoutException)
        {
            return default;
        }
    }
}
