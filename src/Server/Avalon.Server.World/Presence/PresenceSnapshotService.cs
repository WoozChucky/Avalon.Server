using Avalon.Infrastructure;
using Avalon.Infrastructure.Presence;
using Avalon.World.Presence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Avalon.Server.World.Presence;

/// <summary>
/// Publishes a live view of who is in which instance, for admin observability tooling.
///
/// The snapshot itself is taken on the tick (#639): <see cref="PresenceCapture" />, which <c>WorldServer.Update</c>
/// runs in the serial phase after the world update about once a second. This service only writes it, deliberately
/// NOT on the 60 Hz simulation tick, so a slow Redis round trip never stalls the world update. It polls every
/// <see cref="PollInterval" /> and writes each snapshot it takes once; it never reads simulation state, so a defect
/// here cannot corrupt gameplay.
///
/// Keys carry a short TTL (<see cref="CacheKeys.PresenceTtl"/>) refreshed on every write. If this server dies or
/// its tick hangs, no new snapshot is captured, nothing is written, and its players simply disappear from the admin
/// view. That is the intended failure mode: show nothing rather than present a stale position as if it were live.
/// </summary>
public sealed class PresenceSnapshotService : BackgroundService
{
    /// <summary>
    /// How often the writer looks for a new snapshot. A quarter of the capture interval, so a snapshot waits at most
    /// that long for its write; a poll that finds nothing costs one exchange.
    /// </summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly PresenceCapture _capture;
    private readonly IReplicatedCache _cache;
    private readonly ILogger<PresenceSnapshotService> _logger;

    public PresenceSnapshotService(
        PresenceCapture capture,
        IReplicatedCache cache,
        ILogger<PresenceSnapshotService> logger)
    {
        _capture = capture;
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await WriteLatestAsync(stoppingToken);
        }
    }

    /// <summary>
    /// Writes the latest snapshot the tick captured, if there is one it has not written. Public so tests can drive a
    /// single cycle without waiting on the timer. Never throws — a failed write is logged and the next capture is
    /// written. Writes are whole-value overwrites, so nothing accumulates.
    /// </summary>
    public async Task WriteLatestAsync(CancellationToken ct)
    {
        WorldPresenceSnapshot? snapshot = _capture.Take();
        if (snapshot is null)
            return;

        try
        {
            await _cache.SetAsync(
                CacheKeys.WorldPresence(snapshot.WorldId),
                PresenceJson.Serialize(snapshot),
                CacheKeys.PresenceTtl);

            await WriteCharacterIndexAsync(snapshot);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Only our own caller's shutdown token should unwind out of this hosted
            // service. Any other cancellation observed here (e.g. a Redis client
            // surfacing a TaskCanceledException from a reconnect/dispose race, which
            // derives from OperationCanceledException) falls through to the logging
            // catch below instead of propagating into BackgroundService and stopping
            // the world server.
            throw;
        }
        catch (Exception ex)
        {
            // Observability must never take the world server down. The next capture is written.
            _logger.LogWarning(ex, "Presence snapshot write failed for world {WorldId}", snapshot.WorldId);
        }
    }

    /// <summary>
    /// Writes the character -> (world, instance) reverse index used by the Api to find a
    /// player without scanning the world's snapshot. Each key names this server's world, since
    /// character ids are unique only per world (#556). Fired off in parallel rather than
    /// awaited one at a time — at hundreds of concurrent players, serial round trips could
    /// eat a meaningful fraction of the 1-second capture budget and risk missing the
    /// <see cref="CacheKeys.PresenceTtl"/> window on a latency spike.
    /// </summary>
    private Task WriteCharacterIndexAsync(WorldPresenceSnapshot snapshot)
    {
        List<Task> writes = [];
        foreach (InstancePresenceSnapshot instance in snapshot.Instances)
        {
            foreach (CharacterPresenceSnapshot c in instance.Characters)
            {
                writes.Add(_cache.SetAsync(
                    CacheKeys.CharacterPresenceIndex(snapshot.WorldId, c.CharacterId),
                    PresenceJson.Serialize(new CharacterPresenceIndex(snapshot.WorldId, instance.InstanceId)),
                    CacheKeys.PresenceTtl));
            }
        }

        return Task.WhenAll(writes);
    }
}
