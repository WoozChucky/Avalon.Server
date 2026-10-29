using System.Threading.Channels;
using Avalon.Crashes.Persistence;
using Avalon.Crashes.Validation;
using Avalon.Crashes.Worker.Storage;

namespace Avalon.Crashes.Worker.Discovery;

public sealed record CrashNoticeRequest(string? Key);

/// <summary>
/// Upload-complete notices (#1252) waiting to be checked: the internal endpoint offers a key, <see cref="CrashNoticeService" />
/// drains them. Bounded: a burst beyond <see cref="Capacity" /> is dropped, and the poll finds those reports instead.
/// </summary>
public sealed class CrashNotices
{
    public const int Capacity = 64;

    private readonly Channel<string> _keys = Channel.CreateBounded<string>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });   // TryWrite never waits: full is false

    /// <summary>False for a key no slot could have, or with no room left.</summary>
    public bool Offer(string? key) => key is not null && CrashObjectKey.TryParse(key, out _) && _keys.Writer.TryWrite(key);

    internal ChannelReader<string> Reader => _keys.Reader;
}

/// <summary>
/// Queues a noticed report at once: HEAD the object (the proof the upload happened, which the API that took the notice
/// cannot see), then <see cref="ICrashQueue.DiscoverAsync" />, idempotent with the poll. A missing object is nothing.
/// </summary>
public sealed class CrashNoticeService(CrashNotices notices, ICrashObjectStore objects, ICrashQueue queue, TimeProvider time,
    ILogger<CrashNoticeService> logger) : BackgroundService
{
    private static readonly TimeSpan s_reportLifetime = TimeSpan.FromDays(90);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (string key in notices.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await QueueAsync(key, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                // Storage or the database is away: the poll finds the report once they are back.
                logger.LogInformation("Crash notice not queued {reason}", e.GetType().Name);
            }
        }
    }

    public async Task<bool> QueueAsync(string key, CancellationToken ct)
    {
        if (await objects.HeadAsync(key, ct) is not { } source)
            return false;
        DateTimeOffset now = time.GetUtcNow();
        return source.LastModified.ToUniversalTime() > now - s_reportLifetime && await queue.DiscoverAsync(source, now, ct);
    }
}
