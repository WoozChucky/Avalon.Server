using Avalon.Crashes.Persistence;
using Avalon.Crashes.Worker.Discovery;
using Avalon.Crashes.Worker.Storage;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Avalon.Crashes.Worker.UnitTests.Discovery;

public sealed class CrashDiscoveryShould
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private static StoredCrashObject Object(string key, DateTimeOffset? uploaded = null) => new(key, "\"etag\"", 10, uploaded ?? s_now);

    [Fact]
    public async Task Refresh_time_between_candidates_before_retention_and_queue_calls()
    {
        var clock = new FakeTimeProvider(s_now);
        var source = new Objects((_, delimiter, _) => delimiter == "/" ? new([], [], null)
            : new([Object("first"), Object("expiring", s_now.AddDays(-90).AddSeconds(1)), Object("third")], [], null));
        var queue = new Queue { OnDiscover = () => clock.Advance(TimeSpan.FromSeconds(2)) };
        await new CrashDiscovery(source, new MemoryStore(), queue, clock).RunCycleAsync(s_now, default);
        Assert.Equal(new[] { "first", "third" }, queue.Seen.Select(x => x.Key));
        Assert.Equal(new[] { s_now, s_now.AddSeconds(2) }, queue.DiscoveryTimes);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("inventory")]
    [InlineData("recent")]
    public async Task Skip_objects_expiring_during_lists(string delayedListing)
    {
        var clock = new FakeTimeProvider(s_now);
        StoredCrashObject expiring = Object("expiring", s_now.AddDays(-90).AddSeconds(1));
        var source = new Objects((prefix, delimiter, _) =>
        {
            string listing = prefix.Length == 0 ? delimiter == "/" ? "root" : "inventory" : "recent";
            if (listing == delayedListing)
            {
                clock.Advance(TimeSpan.FromSeconds(2));
                return new([expiring], [], null);
            }
            return new([], delayedListing == "recent" && listing == "root" ? ["0.1.0/"] : [], null);
        });
        var queue = new Queue();
        await new CrashDiscovery(source, new MemoryStore(), queue, clock).RunCycleAsync(s_now, default);
        Assert.Empty(queue.Seen);
    }

    [Fact]
    public async Task Should_reserve_backfill_work_under_continuous_current_uploads()
    {
        var source = new Objects((prefix, delimiter, token) =>
        {
            if (delimiter == "/")
                return new([], prefix.Length == 0 ? ["0.1.0/"] : ["0.1.0/2026-10-23/"], null);
            if (prefix.Length == 0)
            {
                int page = token is null ? 0 : int.Parse(token);
                return new([Object($"old/2026-09-01/{page:D32}.zip")], [], page == 79 ? null : (page + 1).ToString());
            }
            return new([Object(prefix + Guid.NewGuid().ToString("N") + ".zip")], [], Guid.NewGuid().ToString("N"));
        });
        var cursor = new MemoryStore();
        var queue = new Queue();
        var discovery = new CrashDiscovery(source, cursor, queue);

        for (int cycle = 0; cycle < 4; cycle++)
        {
            source.Requests.Clear();
            await discovery.RunCycleAsync(s_now.AddMinutes(cycle), default);
            Assert.InRange(source.Requests.Count, 25, 100);
            if (cycle < 3)
                Assert.True(source.Requests.Count(x => x.Prefix.Length == 0) >= 25);
        }
        Assert.Contains(queue.Seen, x => x.Key == $"old/2026-09-01/{79:D32}.zip");
        Assert.NotNull(cursor.Value.LastBackfillAt);
    }

    [Theory]
    [InlineData("0.1.0/2026-10-23/11111111111111111111111111111111.zip")]
    [InlineData("0.0.1/2026-07-25/22222222222222222222222222222222.zip")]
    [InlineData("not/a/conforming/key")]
    [InlineData("orphan.zip")]
    public async Task Discover_unchanged_keys_from_inventory_independent_of_manifests(string key)
    {
        var source = new Objects((_, delimiter, _) => delimiter is null ? new([Object(key)], [], null) : new([], [], null));
        var queue = new Queue();
        await new CrashDiscovery(source, new MemoryStore(), queue).RunCycleAsync(s_now, default);
        Assert.Contains(queue.Seen, x => x.Key == key);
    }

    [Fact]
    public async Task Skip_expired_objects_using_storage_time_instead_of_key_date()
    {
        StoredCrashObject late = Object("old/2020-01-01/11111111111111111111111111111111.zip");
        var source = new Objects((_, delimiter, _) => delimiter is null
            ? new([Object("new/2026-10-23/22222222222222222222222222222222.zip", s_now.AddDays(-90)), late], [], null)
            : new([], [], null));
        var queue = new Queue();
        await new CrashDiscovery(source, new MemoryStore(), queue).RunCycleAsync(s_now, default);
        Assert.Equal(late, Assert.Single(queue.Seen));
    }

    [Fact]
    public async Task Refresh_version_inventory_and_round_robin_dates_across_versions()
    {
        var source = new Objects((prefix, delimiter, token) => delimiter == "/"
            ? prefix.Length == 0
                ? token is null ? new([], ["0.1.0/"], "root-next") : new([], ["0.0.1/"], null)
                : new([], [prefix + "2026-10-23/"], token is null ? "dates-next" : null)
            : prefix.Length == 0 ? new([], [], null) : new([Object(prefix + "11111111111111111111111111111111.zip")], [], token is null ? "objects-next" : null));
        var store = new MemoryStore();
        var queue = new Queue();
        var discovery = new CrashDiscovery(source, store, queue);
        await discovery.RunCycleAsync(s_now, default);
        await discovery.RunCycleAsync(s_now.AddMinutes(1), default);
        Assert.Contains(source.Requests, r => r.Prefix == "0.1.0/2026-10-23/");
        Assert.Contains(source.Requests, r => r.Prefix == "0.0.1/2026-10-22/");
        Assert.Contains(source.Requests, r => r.Prefix == "0.0.1/" && r.Token == "dates-next");
        Assert.Contains(source.Requests, r => r.Prefix == "0.1.0/2026-10-23/" && r.Token == "objects-next");
        Assert.DoesNotContain(source.Requests, r => r.Prefix.StartsWith("crashes/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Revisit_completed_sweeps_for_late_arrivals_and_changed_sources()
    {
        StoredCrashObject candidate = Object("0.1.0/2026-10-22/11111111111111111111111111111111.zip");
        var source = new Objects((_, delimiter, _) => delimiter is null ? new([candidate], [], null) : new([], [], null));
        var store = new MemoryStore();
        var queue = new Queue();
        var discovery = new CrashDiscovery(source, store, queue);
        await discovery.RunCycleAsync(s_now, default);
        candidate = candidate with { ETag = "changed", LastModified = s_now.AddMinutes(1) };
        await discovery.RunCycleAsync(s_now.AddMinutes(1), default);
        Assert.Equal(2, queue.Seen.Count);
        Assert.Equal("changed", queue.Seen[1].ETag);
    }

    [Fact]
    public async Task Replay_partial_pages_after_a_queue_failure_without_advancing_cursor()
    {
        var source = new Objects((_, delimiter, _) => delimiter is null
            ? new([Object("first"), Object("second")], [], "next") : new([], [], null));
        var store = new MemoryStore();
        var queue = new Queue { FailOn = "second" };
        var discovery = new CrashDiscovery(source, store, queue);
        await Assert.ThrowsAsync<IOException>(() => discovery.RunCycleAsync(s_now, default));
        Assert.Null(store.Value.BackfillContinuationToken);
        queue.FailOn = null;
        await discovery.RunCycleAsync(s_now, default);
        Assert.True(queue.Seen.Count(x => x.Key == "first") >= 2);
    }

    [Fact]
    public async Task Walk_historical_dates_newest_first_with_the_retention_boundary_day()
    {
        var source = new Objects((prefix, delimiter, _) => delimiter == "/"
            ? new([], prefix.Length == 0 ? ["0.1.0/"] : Enumerable.Range(2, 90).Select(day => "0.1.0/" + s_now.AddDays(-day).ToString("yyyy-MM-dd") + "/").ToArray(), null)
            : new([], [], null));
        var cursor = new MemoryStore();
        var discovery = new CrashDiscovery(source, cursor, new Queue());
        await discovery.RunCycleAsync(s_now, default);
        Assert.Contains(source.Requests, x => x.Prefix == "0.1.0/2026-10-21/");
        Assert.Contains(source.Requests, x => x.Prefix == "0.1.0/2026-10-20/");
        for (int cycle = 1; cycle < 6; cycle++)
            await discovery.RunCycleAsync(s_now.AddMinutes(cycle), default);
        Assert.Contains(source.Requests, x => x.Prefix == "0.1.0/2026-07-24/");
    }

    [Fact]
    public async Task Paginate_sparse_date_inventory_before_visiting_newest_known_historical_date()
    {
        var source = new Objects((prefix, delimiter, token) => delimiter == "/"
            ? prefix.Length == 0 ? new([], ["0.1.0/"], null)
                : token is null ? new([], ["0.1.0/2026-08-01/", "0.1.0/2020-01-01/", "0.1.0/bad/"], "next-dates")
                : new([], ["0.1.0/2026-10-20/"], null)
            : new([], [], null));
        await new CrashDiscovery(source, new MemoryStore(), new Queue()).RunCycleAsync(s_now, default);
        string[] historical = source.Requests.Where(x => x.Delimiter is null && x.Prefix.Length > 0
            && !x.Prefix.EndsWith("2026-10-23/", StringComparison.Ordinal) && !x.Prefix.EndsWith("2026-10-22/", StringComparison.Ordinal)).Select(x => x.Prefix).ToArray();
        Assert.Equal("0.1.0/2026-10-20/", historical[0]);
        Assert.Contains("0.1.0/2026-08-01/", historical);
        Assert.DoesNotContain(historical, x => x.Contains("2020", StringComparison.Ordinal) || x.Contains("bad", StringComparison.Ordinal));
        Assert.Equal(2, historical.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Move_todays_opaque_token_to_yesterday_on_utc_midnight()
    {
        var source = new Objects((_, _, _) => new([], [], null));
        var store = new MemoryStore
        {
            Value = new DiscoveryCursor
            {
                Versions = [new DiscoveryVersion { Version = "0.1.0", RecentDate = new DateOnly(2026, 10, 22), TodayToken = "prior-today", YesterdayToken = "discard", Phase = 2 }]
            }
        };
        await new CrashDiscovery(source, store, new Queue()).RunCycleAsync(s_now, default);
        Assert.Contains(source.Requests, x => x.Prefix == "0.1.0/2026-10-22/" && x.Token == "prior-today");
        Assert.DoesNotContain(source.Requests, x => x.Token == "discard");
    }

    [Theory]
    [InlineData("../")]
    [InlineData("bad name/")]
    [InlineData("nested/version/")]
    public async Task Keep_malformed_root_prefixes_out_of_scheduler_state(string prefix)
    {
        var source = new Objects((_, delimiter, _) => delimiter is null ? new([], [], null) : new([], [prefix], null));
        var store = new MemoryStore();
        await new CrashDiscovery(source, store, new Queue()).RunCycleAsync(s_now, default);
        Assert.Empty(store.Value.Versions);
        Assert.Empty(store.Value.DiscoveredVersions);
    }

    [Fact]
    public async Task Reset_invalid_opaque_tokens_and_resume_without_clearing_history()
    {
        var source = new Objects((_, delimiter, token) => token == "stale"
            ? throw new CrashContinuationTokenException()
            : delimiter is null ? new([Object("late")], [], null) : new([], [], null));
        var store = new MemoryStore { Value = new DiscoveryCursor { ContinuationToken = "stale", BackfillContinuationToken = "stale" } };
        var queue = new Queue();
        await new CrashDiscovery(source, store, queue).RunCycleAsync(s_now, default);
        await new CrashDiscovery(source, store, queue).RunCycleAsync(s_now.AddMinutes(1), default);
        Assert.Contains(queue.Seen, x => x.Key == "late");
        Assert.Null(store.Value.ContinuationToken);
        Assert.Null(store.Value.BackfillContinuationToken);
    }

    private sealed class Objects(Func<string, string?, string?, CrashObjectPage> list) : ICrashObjectStore
    {
        public List<(string Prefix, string? Delimiter, string? Token)> Requests { get; } = [];
        public Task<CrashObjectPage> ListAsync(string prefix, string? delimiter, string? continuationToken, CancellationToken ct)
        {
            Requests.Add((prefix, delimiter, continuationToken));
            return Task.FromResult(list(prefix, delimiter, continuationToken));
        }
        public Task<StoredCrashObject?> HeadAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task DownloadAsync(StoredCrashObject expected, string destination, long maxBytes, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class MemoryStore : ICrashDiscoveryStore
    {
        public DiscoveryCursor Value { get; set; } = new();
        public Task<DiscoveryCursor> LoadAsync(CancellationToken ct) => Task.FromResult(Value with { Versions = Value.Versions.Select(x => x with { }).ToArray() });
        public Task SaveAsync(DiscoveryCursor cursor, CancellationToken ct)
        {
            Value = cursor with { Versions = cursor.Versions.Select(x => x with { }).ToArray() };
            return Task.CompletedTask;
        }
    }

    private sealed class Queue : ICrashQueue
    {
        public List<StoredCrashObject> Seen { get; } = [];
        public List<DateTimeOffset> DiscoveryTimes { get; } = [];
        public Action? OnDiscover { get; init; }
        public string? FailOn { get; set; }
        public Task<bool> DiscoverAsync(StoredCrashObject source, DateTimeOffset now, CancellationToken ct)
        {
            if (source.Key == FailOn)
                throw new IOException();
            Seen.Add(source);
            DiscoveryTimes.Add(now);
            OnDiscover?.Invoke();
            return Task.FromResult(true);
        }
        public Task<CrashLease?> ClaimAsync(DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> RenewAsync(CrashLease lease, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> CompleteAsync(CrashLease lease, CrashAttemptResult result, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
    }
}
