using Avalon.Crashes.Persistence;
using Avalon.Crashes.Worker.Diagnostics;
using Avalon.Crashes.Worker.Maintenance;
using Avalon.Crashes.Worker.Processing;
using Avalon.Crashes.Worker.Storage;
using Avalon.Crashes.Worker.Symbols;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Avalon.Crashes.Worker.UnitTests.Hosting;

public sealed class CrashWorkerHostShould
{
    [Fact]
    public async Task Should_keep_a_required_symbols_outage_degraded_until_that_dependency_recovers()
    {
        var clock = new FakeTimeProvider();
        var state = new CrashWorkerState(clock) { ToolsReady = true, StorageReady = true };
        state.Purged(state.DatabaseGeneration);
        var inner = new Symbols();
        var store = new CrashHealthySymbolStore(inner, state);
        var health = new CrashHealthCheck(state, new());
        await Assert.ThrowsAsync<CrashSymbolException>(() => store.DownloadOriginalAsync(null!, false, "unused", 1, new(), default));
        Assert.False(state.IntakeReady);
        // A successful crash-bucket LIST is not a symbol-store probe.
        state.StorageReady = true;
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True(state.IntakeReady);
        Assert.Equal(HealthStatus.Unhealthy, (await health.CheckHealthAsync(new())).Status);
        inner.Available = true;
        await store.DownloadOriginalAsync(null!, false, "unused", 1, new(), default);
        Assert.Equal(HealthStatus.Healthy, (await health.CheckHealthAsync(new())).Status);
        await Assert.ThrowsAsync<CrashSymbolException>(() => store.UploadConvertedAsync(null!, null!, "unused", default));
        Assert.Equal(HealthStatus.Healthy, (await health.CheckHealthAsync(new())).Status);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Should_expose_internal_health_during_migration_and_when_paused(string environment)
    {
        var migration = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using WebApplication app = CrashWorkerRegistration.BuildRun(true, builder =>
        {
            builder.Environment.EnvironmentName = environment;
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Crashes:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=graph_only",
                ["Crashes:Storage:Endpoint"] = "https://localhost:1",
                ["Crashes:Storage:AccessKeyId"] = "private-key",
                ["Crashes:Storage:SecretAccessKey"] = "private-secret"
            });
            builder.Services.Replace(ServiceDescriptor.Singleton<ICrashWorkerRuntime>(s => new Runtime(s.GetRequiredService<CrashWorkerState>()) { Migration = migration.Task }));
        });
        // Construct the complete production graph without connecting or executing native tools.
        _ = app.Services.GetRequiredService<CrashProcessor>();
        Assert.Same(app.Services.GetRequiredService<CrashWorkspace>(), app.Services.GetRequiredService<CrashWorkspace>());
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:8080") };
        Assert.Equal("Healthy", await http.GetStringAsync("/alive"));
        using HttpResponseMessage unready = await http.GetAsync("/health");
        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, unready.StatusCode);
        Assert.Equal("Unhealthy", await unready.Content.ReadAsStringAsync());
        migration.SetResult();
        CrashWorkerState state = app.Services.GetRequiredService<CrashWorkerState>();
        await UntilAsync(() => state.DatabaseReady && state.ToolsReady);
        Assert.Equal("Healthy", await http.GetStringAsync("/health"));
        await app.StopAsync();
    }

    [Fact]
    public async Task Should_keep_degraded_readiness_after_backoff_expires_until_a_successful_probe()
    {
        var clock = new FakeTimeProvider();
        var state = new CrashWorkerState(clock) { ToolsReady = true, StorageReady = true };
        state.Purged(state.DatabaseGeneration);
        var backoff = new CrashProcessingBackoff();
        var health = new CrashHealthCheck(state, backoff);
        backoff.Degrade(CrashFailureCode.AccessDenied, clock.GetUtcNow());
        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(HealthStatus.Unhealthy, (await health.CheckHealthAsync(new())).Status);
        backoff.Clear();
        Assert.Equal(HealthStatus.Healthy, (await health.CheckHealthAsync(new())).Status);
    }

    [Fact]
    public async Task Should_clean_before_resuming_intake_and_keep_maintenance_alive_during_a_blocked_tool()
    {
        var clock = new FakeTimeProvider();
        var state = new CrashWorkerState(clock);
        var runtime = new Runtime(state);
        var queue = new Queue();
        string scratch = Path.Combine(Path.GetTempPath(), "host-crash-" + Guid.NewGuid().ToString("N"));
        var processor = new CrashProcessor(queue, new Objects(), new CrashWorkspace(new() { ScratchRoot = scratch }), new BlockedPipeline(), new(), new(), clock);
        var lease = new CrashLease(Guid.NewGuid(), Guid.NewGuid(), new("1.2.3/2000-01-01/11111111111111111111111111111111.zip", "etag", 1, clock.GetUtcNow()),
            1, 0, clock.GetUtcNow(), clock.GetUtcNow(), clock.GetUtcNow().AddDays(90), clock.GetUtcNow().AddMinutes(10));
        runtime.Process = async ct => { await processor.ProcessAsync(lease, ct); await Task.Delay(Timeout.Infinite, ct); };
        using var host = new CrashWorkerService(runtime, state, clock);
        await host.StartAsync(default);
        await UntilAsync(() => runtime.Processing > 0);
        Assert.Equal(new[] { "migrate", "cleanup", "validate" }, runtime.Events.Take(3));
        state.DatabaseFailed();
        runtime.FailCleanup = true;
        clock.Advance(TimeSpan.FromMinutes(1));
        await UntilAsync(() => runtime.Cleanups >= 2);
        Assert.False(state.DatabaseReady);
        runtime.FailCleanup = false;
        clock.Advance(TimeSpan.FromMinutes(1));
        await UntilAsync(() => state.DatabaseReady);
        Assert.True(state.Alive);
        Assert.Equal(1, runtime.Processing);
        Assert.True(runtime.Cleanups >= 3);
        Assert.True(queue.Renewals > 0);
        clock.Advance(TimeSpan.FromMinutes(6));
        await UntilAsync(() => queue.CompletionReason == CrashFailureCode.ToolTimeout);
        Assert.True(runtime.Cleanups >= 4);
        await host.StopAsync(default);
        if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
    }

    [Fact]
    public async Task Should_keep_paused_worker_healthy_without_discovery_or_claims()
    {
        var clock = new FakeTimeProvider();
        var state = new CrashWorkerState(clock) { Paused = true };
        var runtime = new Runtime(state);
        using var host = new CrashWorkerService(runtime, state, clock);
        await host.StartAsync(default);
        await UntilAsync(() => state.ToolsReady && state.DatabaseReady);
        clock.Advance(TimeSpan.FromMinutes(1));
        await UntilAsync(() => runtime.Cleanups >= 2);
        Assert.Equal(0, runtime.Processing);
        Assert.Equal(0, runtime.Discoveries);
        await host.StopAsync(default);
    }

    private static async Task UntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!predicate()) await Task.Delay(5, timeout.Token);
    }

    private sealed class Runtime(CrashWorkerState state) : ICrashWorkerRuntime
    {
        public List<string> Events { get; } = [];
        public int Cleanups;
        public int Processing;
        public int Discoveries;
        public bool FailCleanup;
        public Func<CancellationToken, Task>? Process { get; set; }
        public Task Migration { get; init; } = Task.CompletedTask;
        public Task MigrateAsync(CancellationToken ct) { Events.Add("migrate"); return Migration.WaitAsync(ct); }
        public Task ValidateAsync(CancellationToken ct) { Events.Add("validate"); return Task.CompletedTask; }
        public Task CleanupAsync(CancellationToken ct)
        {
            Events.Add("cleanup");
            Interlocked.Increment(ref Cleanups);
            return FailCleanup ? Task.FromException(new IOException("private database error")) : Task.CompletedTask;
        }
        public Task DiscoverAsync(CancellationToken ct) { state.StorageReady = true; Interlocked.Increment(ref Discoveries); return Task.CompletedTask; }
        public Task ProcessOneAsync(CancellationToken ct)
        {
            Assert.True(state.IntakeReady);
            Interlocked.Increment(ref Processing);
            return Process?.Invoke(ct) ?? Task.Delay(Timeout.Infinite, ct);
        }
    }

    private sealed class BlockedPipeline : ICrashAnalysisPipeline
    {
        public async Task<CrashAnalysis> AnalyzeAsync(StoredCrashObject source, CrashAttemptWorkspace workspace, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException();
        }
    }
    private sealed class Queue : ICrashQueue
    {
        public int Renewals;
        public CrashFailureCode? CompletionReason;
        public Task<bool> DiscoverAsync(StoredCrashObject source, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
        public Task<CrashLease?> ClaimAsync(DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> RenewAsync(CrashLease lease, DateTimeOffset now, CancellationToken ct) { Interlocked.Increment(ref Renewals); return Task.FromResult(true); }
        public Task<bool> CompleteAsync(CrashLease lease, CrashAttemptResult result, DateTimeOffset now, CancellationToken ct) { CompletionReason = result.Reason; return Task.FromResult(true); }
    }
    private sealed class Objects : ICrashObjectStore
    {
        public Task<CrashObjectPage> ListAsync(string prefix, string? delimiter, string? continuationToken, CancellationToken ct) => throw new NotSupportedException();
        public Task<StoredCrashObject?> HeadAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task DownloadAsync(StoredCrashObject expected, string destination, long maxBytes, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Symbols : ICrashSymbolStore
    {
        public bool Available;
        public Task DownloadOriginalAsync(ModuleIdentity identity, bool pdb, string destination, long maxBytes, SymbolDownloadBudget budget, CancellationToken ct) =>
            Available ? Task.CompletedTask : Task.FromException(new CrashSymbolException(CrashFailureCode.StorageUnavailable));
        public Task<SymbolCacheMetadata?> DownloadConvertedAsync(ModuleIdentity identity, string cacheVersion, string destination, long maxBytes, SymbolDownloadBudget budget, CancellationToken ct) => throw new NotSupportedException();
        public Task UploadConvertedAsync(ModuleIdentity identity, SymbolCacheMetadata metadata, string source, CancellationToken ct) =>
            Task.FromException(new CrashSymbolException(CrashFailureCode.StorageUnavailable));
    }
}
