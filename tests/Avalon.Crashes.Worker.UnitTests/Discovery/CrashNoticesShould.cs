using Avalon.Crashes.Persistence;
using Avalon.Crashes.Worker.Diagnostics;
using Avalon.Crashes.Worker.Discovery;
using Avalon.Crashes.Worker.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Avalon.Crashes.Worker.UnitTests.Discovery;

/// <summary>
/// #1252: a notice queues a report at once, but only one the bucket holds (the worker's HEAD is the proof the upload
/// happened), and a burst of notices is bounded: what does not fit is left to the poll.
/// </summary>
public class CrashNoticesShould
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 29, 0, 30, 0, TimeSpan.Zero);
    private const string Key = "0.4.1/2026-10-29/0123456789abcdef0123456789abcdef.zip";
    private const string Missing = "0.4.1/2026-10-29/456789abcdef0123456789abcdef0123.zip";

    [Fact]
    public async Task Queue_an_uploaded_report_and_ignore_one_the_bucket_does_not_hold_or_a_paused_worker()
    {
        var stored = new StoredCrashObject(Key, "\"etag\"", 2048, s_now.AddMinutes(-1));
        var queue = new Queue();
        CrashNoticeService service = Service(stored, queue, paused: false);

        Assert.True(await service.QueueAsync(Key, CancellationToken.None));
        Assert.False(await service.QueueAsync(Missing, CancellationToken.None));
        // Paused discovers nothing, as the poll does not: the rollout and the rollback depend on it.
        Assert.False(await Service(stored, queue, paused: true).QueueAsync(Key, CancellationToken.None));
        Assert.Equal([stored], queue.Seen);
    }

    private static CrashNoticeService Service(StoredCrashObject stored, Queue queue, bool paused)
    {
        var clock = new FakeTimeProvider(s_now);
        var state = new CrashWorkerState(clock) { Paused = paused, ToolsReady = true };
        state.Purged(state.DatabaseGeneration);   // the database ready, as after the first purge
        return new CrashNoticeService(new CrashNotices(), new Objects(stored), queue, state, clock, NullLoggerFactory.Instance);
    }

    [Fact]
    public void Take_only_well_formed_keys_and_no_more_than_it_holds()
    {
        var notices = new CrashNotices();

        Assert.False(notices.Offer("../escape.zip"));
        for (int i = 0; i < CrashNotices.Capacity; i++)
            Assert.True(notices.Offer(Key));
        Assert.False(notices.Offer(Key));
    }

    private sealed class Objects(StoredCrashObject held) : ICrashObjectStore
    {
        public Task<CrashObjectPage> ListAsync(string prefix, string? delimiter, string? continuationToken, CancellationToken ct) => throw new NotSupportedException();
        public Task<StoredCrashObject?> HeadAsync(string key, CancellationToken ct) => Task.FromResult(key == held.Key ? held : null);
        public Task DownloadAsync(StoredCrashObject expected, string destination, long maxBytes, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Queue : ICrashQueue
    {
        public List<StoredCrashObject> Seen { get; } = [];
        public Task<bool> DiscoverAsync(StoredCrashObject source, DateTimeOffset now, CancellationToken ct)
        {
            Seen.Add(source);
            return Task.FromResult(true);
        }
        public Task<CrashLease?> ClaimAsync(DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> RenewAsync(CrashLease lease, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> CompleteAsync(CrashLease lease, CrashAttemptResult result, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
    }
}
