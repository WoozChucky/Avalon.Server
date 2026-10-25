using Avalon.Crashes.Persistence;
using Avalon.Crashes.Worker.Processing;
using Avalon.Crashes.Worker.Storage;
using Avalon.Crashes.Worker.Symbols;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Avalon.Crashes.Worker.UnitTests.Processing;

public sealed class CrashProcessorShould
{
    [Fact]
    public async Task Should_claim_only_one_report_until_processing_finishes()
    {
        await using var test = new Harness();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Queue.OnClaim = () => { if (test.Queue.Claims > 1) second.TrySetResult(); return test.Queue.Claims == 1 ? test.Lease : null; };
        test.Pipeline.Run = async (_, ct) => { entered.TrySetResult(); await release.Task.WaitAsync(ct); return Analysis(); };
        using var service = new CrashProcessingService(test.Queue, test.Processor, test.Backoff, test.Clock);
        await service.StartAsync(default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, test.Queue.Claims);
        release.SetResult();
        await second.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(default);
        Assert.Equal(CrashReportStatus.Processed, test.Queue.Result?.Status);
    }

    [Fact]
    public async Task Should_wait_for_global_backoff_before_claiming()
    {
        await using var test = new Harness();
        var claimed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Backoff.Degrade(CrashFailureCode.AccessDenied, test.Clock.GetUtcNow());
        test.Queue.OnClaim = () => { claimed.TrySetResult(); return null; };
        using var service = new CrashProcessingService(test.Queue, test.Processor, test.Backoff, test.Clock);
        await service.StartAsync(default);
        await Task.Delay(50);
        Assert.Equal(0, test.Queue.Claims);
        test.Clock.Advance(TimeSpan.FromMinutes(5));
        await claimed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(test.Backoff.State); // Retry eligibility does not itself assert health recovery.
        await service.StopAsync(default);
    }

    [Fact]
    public async Task Should_process_with_missing_public_symbols()
    {
        await using var test = new Harness();
        await test.ProcessAsync();
        Assert.Equal(CrashReportStatus.Processed, test.Queue.Result?.Status);
        Assert.Equal(CrashQualityFlags.MissingSystemSymbols, test.Queue.Result!.Analysis!.Quality);
        Assert.Empty(Directory.EnumerateDirectories(test.Root));
    }

    [Fact]
    public async Task Should_retry_missing_runtime_symbols_without_creating_a_group()
    {
        await using var test = new Harness();
        test.Pipeline.Run = (_, _) => throw new CrashSymbolException(CrashFailureCode.MissingRuntimeSymbols);
        await test.ProcessAsync();
        Assert.Equal(CrashReportStatus.RetryWait, test.Queue.Result?.Status);
        Assert.Null(test.Queue.Result!.Signature);
        Assert.Empty(Directory.EnumerateDirectories(test.Root));
    }

    [Fact]
    public async Task Should_stop_when_lease_is_lost()
    {
        await using var test = new Harness();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Queue.Renewed = false;
        test.Pipeline.Run = async (_, ct) => { started.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Analysis(); };
        Task work = test.ProcessAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        test.Clock.Advance(TimeSpan.FromSeconds(30));
        await work.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(test.Queue.Result);
        Assert.Equal(1, test.Queue.Renewals);
        Assert.Empty(Directory.EnumerateDirectories(test.Root));
    }

    [Fact]
    public async Task Should_discard_analysis_crossing_expiry()
    {
        await using var test = new Harness();
        test.Lease = test.Lease with { ExpiresAt = test.Clock.GetUtcNow().AddSeconds(1) };
        test.Pipeline.Run = (_, _) => { test.Clock.Advance(TimeSpan.FromSeconds(2)); return Task.FromResult(Analysis()); };
        await test.ProcessAsync();
        Assert.Null(test.Queue.Result);
        Assert.Empty(Directory.EnumerateDirectories(test.Root));
    }

    [Theory]
    [InlineData(false, CrashFailureCode.ObjectMissing, CrashReportStatus.Failed)]
    [InlineData(true, CrashFailureCode.ObjectChanged, CrashReportStatus.Rejected)]
    public async Task Should_discard_content_when_the_source_disappears_or_changes(bool changed, CrashFailureCode reason, CrashReportStatus status)
    {
        await using var test = new Harness();
        test.Objects.Head = changed ? test.Lease.Source with { ETag = "changed" } : null;
        await test.ProcessAsync();
        Assert.Equal(status, test.Queue.Result?.Status);
        Assert.Equal(reason, test.Queue.Result?.Reason);
        Assert.Null(test.Queue.Result!.Analysis);
    }

    [Theory]
    [InlineData(CrashFailureCode.AccessDenied)]
    [InlineData(CrashFailureCode.ConfigurationInvalid)]
    public async Task Should_throttle_configuration_outages_globally_and_clear_after_a_successful_probe(CrashFailureCode reason)
    {
        await using var test = new Harness();
        test.Pipeline.Run = (_, _) => throw new CrashSymbolException(reason);
        await test.ProcessAsync();
        Assert.Equal(CrashReportStatus.RetryWait, test.Queue.Result?.Status);
        Assert.Equal(reason, test.Backoff.State?.Reason);
        Assert.True(test.Backoff.State!.RetryAt > test.Clock.GetUtcNow());
        test.Backoff.Clear();
        Assert.Null(test.Backoff.State);
    }

    [Fact]
    public async Task Should_leave_shutdown_recoverable_without_classifying_an_invalid_archive()
    {
        await using var test = new Harness();
        using var shutdown = new CancellationTokenSource();
        test.Pipeline.Run = (_, ct) => { shutdown.Cancel(); ct.ThrowIfCancellationRequested(); return Task.FromResult(Analysis()); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test.Processor.ProcessAsync(test.Lease, shutdown.Token));
        Assert.Null(test.Queue.Result);
        Assert.Empty(Directory.EnumerateDirectories(test.Root));
    }

    [Fact]
    public async Task Should_bound_the_attempt_even_when_a_dependency_waits()
    {
        await using var test = new Harness();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Pipeline.Run = async (_, ct) => { started.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Analysis(); };
        Task work = test.ProcessAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        test.Clock.Advance(TimeSpan.FromMinutes(8));
        await work.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(CrashFailureCode.ToolTimeout, test.Queue.Result?.Reason);
        Assert.Equal(CrashReportStatus.RetryWait, test.Queue.Result?.Status);
    }

    [Fact]
    public async Task Should_cancel_and_leave_work_recoverable_when_heartbeat_database_calls_fail()
    {
        await using var test = new Harness();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Queue.RenewException = new TimeoutException();
        test.Pipeline.Run = async (_, ct) => { started.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Analysis(); };
        Task work = test.ProcessAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        test.Clock.Advance(TimeSpan.FromSeconds(30));
        await Assert.ThrowsAsync<TimeoutException>(() => work.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Null(test.Queue.Result);
        Assert.Empty(Directory.GetDirectories(test.Root));
    }

    [Fact]
    public async Task Should_propagate_persistent_database_completion_failure_without_reclassifying_it()
    {
        await using var test = new Harness();
        test.Queue.CompletionException = new TimeoutException();
        await Assert.ThrowsAsync<TimeoutException>(() => test.ProcessAsync());
        Assert.Equal(2, test.Queue.Completions);
        Assert.Equal(CrashReportStatus.Processed, test.Queue.Result?.Status);
        Assert.Equal(1, test.Pipeline.Calls);
        Assert.Empty(Directory.GetDirectories(test.Root));
    }

    [Fact]
    public async Task Should_bound_successful_finalization_but_reconcile_a_commit_after_timeout()
    {
        await using var test = new Harness();
        test.Queue.OnComplete = (result, ct) =>
        {
            Assert.Equal(test.Lease.FirstAttemptAt.AddMinutes(8), result.ProcessedDeadline);
            if (test.Queue.Completions == 1)
            {
                test.Clock.Advance(TimeSpan.FromMinutes(8));
                Assert.True(ct.IsCancellationRequested);
                throw new OperationCanceledException(ct);
            }
            Assert.False(ct.IsCancellationRequested);
            return Task.FromResult(true);
        };
        await test.ProcessAsync();
        Assert.Equal(2, test.Queue.Completions);
        Assert.Equal(CrashReportStatus.Processed, test.Queue.Result?.Status);
    }

    [Fact]
    public async Task Should_resume_after_uncertain_commit_without_recomputing_analysis()
    {
        await using var test = new Harness();
        test.Queue.UncertainOnce = true;
        Task work = test.ProcessAsync();
        await test.Queue.FirstCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        test.Clock.Advance(TimeSpan.FromSeconds(1));
        await work.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, test.Queue.Completions);
        Assert.Equal(1, test.Pipeline.Calls);
        Assert.Equal(CrashReportStatus.Processed, test.Queue.Result?.Status);
    }

    internal static CrashAnalysis Analysis() => new(1, new("EXCEPTION_ACCESS_VIOLATION_READ", 0xc0000005, "0x0000000000001000", 0, 1),
        new("Windows NT", "10", "19045", "amd64", "cpu"),
        [new(0, 1, [new("runtime.exe", "0x0000000000000010", "Game::Tick", null, null, [], "context") { ImageKey = "12341000" }])],
        [new("runtime.exe", "12341000", "runtime.pdb", "ABCD1", "1.0.0", "0x0000000000001000", "0x0000000000001000", true)],
        new(1, "runtime", "1.0.0", "abc", 0xc0000005, "1000", 1, DateTimeOffset.UtcNow), null,
        CrashQualityFlags.MissingSystemSymbols, new("0.27.0", "2.3.9", "v1"));

    private sealed class Harness : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "crash-processor-" + Guid.NewGuid().ToString("N"));
        public FakeTimeProvider Clock { get; }
        public CrashLease Lease { get; set; } = CrashRetryPolicyShould.Lease();
        public Queue Queue { get; } = new();
        public Pipeline Pipeline { get; } = new();
        public Objects Objects { get; }
        public CrashProcessingBackoff Backoff { get; } = new();
        public CrashProcessor Processor { get; }
        public Harness()
        {
            Clock = new(Lease.FirstAttemptAt);
            Objects = new() { Head = Lease.Source };
            Processor = new(Queue, Objects, new(new() { ScratchRoot = Root }), Pipeline, new(), Backoff, Clock);
        }
        public Task ProcessAsync() => Processor.ProcessAsync(Lease, default);
        public ValueTask DisposeAsync() { if (Directory.Exists(Root)) Directory.Delete(Root, true); return ValueTask.CompletedTask; }
    }

    private sealed class Pipeline : ICrashAnalysisPipeline
    {
        public Func<CrashAttemptWorkspace, CancellationToken, Task<CrashAnalysis>> Run { get; set; } = (_, _) => Task.FromResult(Analysis());
        public int Calls { get; private set; }
        public Task<CrashAnalysis> AnalyzeAsync(StoredCrashObject source, CrashAttemptWorkspace workspace, CancellationToken ct)
        { Calls++; return Run(workspace, ct); }
    }

    private sealed class Objects : ICrashObjectStore
    {
        public StoredCrashObject? Head { get; set; }
        public Task<StoredCrashObject?> HeadAsync(string key, CancellationToken ct) => Task.FromResult(Head);
        public Task DownloadAsync(StoredCrashObject expected, string destination, long maxBytes, CancellationToken ct) => throw new NotSupportedException();
        public Task<CrashObjectPage> ListAsync(string prefix, string? delimiter, string? continuationToken, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Queue : ICrashQueue
    {
        public Func<CrashLease?> OnClaim { get; set; } = () => null;
        public int Claims { get; private set; }
        public CrashAttemptResult? Result { get; private set; }
        public bool Renewed { get; set; } = true;
        public int Renewals { get; private set; }
        public int Completions { get; private set; }
        public bool UncertainOnce { get; set; }
        public Exception? RenewException { get; set; }
        public Exception? CompletionException { get; set; }
        public Func<CrashAttemptResult, CancellationToken, Task<bool>>? OnComplete { get; set; }
        public TaskCompletionSource FirstCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> RenewAsync(CrashLease lease, DateTimeOffset now, CancellationToken ct)
        { Renewals++; if (RenewException is not null) throw RenewException; return Task.FromResult(Renewed); }
        public Task<bool> CompleteAsync(CrashLease lease, CrashAttemptResult result, DateTimeOffset now, CancellationToken ct)
        {
            Completions++; Result = result;
            if (OnComplete is not null) return OnComplete(result, ct);
            if (CompletionException is not null) throw CompletionException;
            if (UncertainOnce && Completions == 1) { FirstCompletion.SetResult(); throw new TimeoutException(); }
            return Task.FromResult(true);
        }
        public Task<CrashLease?> ClaimAsync(DateTimeOffset now, CancellationToken ct) { Claims++; return Task.FromResult(OnClaim()); }
        public Task<bool> DiscoverAsync(StoredCrashObject source, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
    }
}
