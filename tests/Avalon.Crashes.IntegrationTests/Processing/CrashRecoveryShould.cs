using System.Diagnostics;
using Avalon.Crashes.Analysis;
using Avalon.Crashes.Persistence;
using Avalon.Crashes.Worker.Processing;
using Avalon.Crashes.Worker.Storage;
using Avalon.Database.Crashes;
using Avalon.Database.Crashes.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Crashes.IntegrationTests.Processing;

public sealed class CrashRecoveryShould : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    public Task InitializeAsync() => _postgres.InitializeAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync();

    [IntegrationTheory]
    [InlineData("holding-lease")]
    [InlineData("before-completion")]
    [InlineData("after-commit")]
    public async Task Should_recover_a_killed_processor_without_recounting_or_retaining_scratch(string stage)
    {
        string root = Path.Combine(Path.GetTempPath(), "crash-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var clock = new RecoveryClock();
        var queue = new CrashQueue(_postgres.Factory, clock);
        StoredCrashObject source = RecoveryData.Source;
        await queue.DiscoverAsync(source, clock.GetUtcNow(), default);
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("vstest");
        start.ArgumentList.Add(typeof(CrashRecoveryShould).Assembly.Location);
        start.ArgumentList.Add("--TestCaseFilter:FullyQualifiedName~RecoveryTestProcessShould.Run_child_processor");
        start.Environment["AVALON_CRASHES_CHILD_CONNECTION"] = db.Database.GetConnectionString();
        start.Environment["AVALON_CRASHES_CHILD_ROOT"] = root;
        start.Environment["AVALON_CRASHES_CHILD_STAGE"] = stage;
        using Process child = Process.Start(start)!;
        Task<string> output = child.StandardOutput.ReadToEndAsync();
        Task<string> errors = child.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (!File.Exists(Path.Combine(root, "ready")) && !child.HasExited)
                await Task.Delay(25, timeout.Token);
            Assert.False(child.HasExited, "Child exited before reaching its injected interruption: " + await CompletedOutputAsync(output, errors, child));
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(output, errors);

            var workspace = new CrashWorkspace(new() { ScratchRoot = Path.Combine(root, "scratch") });
            if (stage == "holding-lease")
                Assert.NotEmpty(Directory.GetDirectories(Path.Combine(root, "scratch")));
            await workspace.CleanupStaleAsync(default);
            Assert.Empty(Directory.GetDirectories(Path.Combine(root, "scratch")));

            clock.Now = clock.Now.AddMinutes(11);
            CrashLease? recovered = await queue.ClaimAsync(clock.GetUtcNow(), default);
            if (stage == "after-commit")
            {
                Assert.Null(recovered);
                CrashReport accepted = await db.Reports.AsNoTracking().SingleAsync();
                var acceptedLease = new CrashLease(accepted.Id, accepted.CompletionToken!.Value, source, accepted.AttemptCount,
                    accepted.ConsecutiveToolFailures, accepted.FirstAttemptAt!.Value, accepted.RetryCampaignStartedAt!.Value, accepted.ExpiresAt, clock.Now.AddMinutes(10));
                Assert.True(await queue.CompleteAsync(acceptedLease, RecoveryData.Result(), clock.Now, default));
            }
            else
            {
                Assert.NotNull(recovered);
                Assert.Equal(2, recovered.AttemptCount);
                Assert.Equal(2, recovered.RetryCampaignAttemptCount);
                Assert.Equal(1, recovered.ConsecutiveToolFailures);
                await new CrashProcessor(queue, new RecoveryObjects(source), workspace, new RecoveryPipeline(), new(), new(), clock)
                    .ProcessAsync(recovered, default);
            }
            CrashReport report = await db.Reports.AsNoTracking().SingleAsync();
            Assert.Equal(CrashReportStatus.Processed, report.Status);
            Assert.Equal(1L, (await db.Groups.AsNoTracking().SingleAsync()).ReportCount);
            Assert.Empty(Directory.GetDirectories(Path.Combine(root, "scratch")));
        }
        finally
        {
            if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
            Directory.Delete(root, true);
        }
    }

    [IntegrationFact]
    public async Task Should_refuse_a_new_success_after_attempt_deadline_but_reconcile_an_accepted_token()
    {
        var clock = new RecoveryClock();
        var queue = new CrashQueue(_postgres.Factory, clock);
        StoredCrashObject source = RecoveryData.Source;
        await queue.DiscoverAsync(source, clock.Now, default);
        CrashLease lease = (await queue.ClaimAsync(clock.Now, default))!;
        CrashAnalysis analysis = RecoveryData.Analysis();
        var result = CrashAttemptResult.Processed(analysis, CrashSignature.Create(analysis), clock.Now.AddMinutes(8));
        Assert.False(await queue.CompleteAsync(lease, result, clock.Now.AddMinutes(8), default));
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        Assert.Empty(await db.Groups.ToListAsync());
        Assert.Equal(CrashReportStatus.Processing, (await db.Reports.AsNoTracking().SingleAsync()).Status);

        Assert.True(await queue.CompleteAsync(lease, result, clock.Now.AddMinutes(7), default));
        Assert.True(await queue.CompleteAsync(lease, result, clock.Now.AddMinutes(8), default));
        Assert.Equal(1L, (await db.Groups.SingleAsync()).ReportCount);
    }

    [IntegrationFact]
    public async Task Should_resume_after_uncertain_commit_without_recounting()
    {
        var clock = new RecoveryClock();
        var queue = new CrashQueue(_postgres.Factory, clock);
        await queue.DiscoverAsync(RecoveryData.Source, clock.Now, default);
        CrashLease lease = (await queue.ClaimAsync(clock.Now, default))!;
        string root = Path.Combine(Path.GetTempPath(), "crash-uncertain-" + Guid.NewGuid().ToString("N"));
        try
        {
            var uncertain = new RecoveryQueue(queue, "uncertain", root);
            await new CrashProcessor(uncertain, new RecoveryObjects(lease.Source), new(new() { ScratchRoot = root }), new RecoveryPipeline(), new(), new(), clock)
                .ProcessAsync(lease, default);
            Assert.Equal(2, uncertain.Completions);
            await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
            Assert.Equal(CrashReportStatus.Processed, (await db.Reports.SingleAsync()).Status);
            Assert.Equal(1L, (await db.Groups.SingleAsync()).ReportCount);
            Assert.Empty(Directory.GetDirectories(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static async Task<string> CompletedOutputAsync(Task<string> output, Task<string> errors, Process child)
        => child.HasExited ? await output + await errors : "";
}

// A separate testhost process is killed by its parent at a controlled durable boundary.
public sealed class RecoveryTestProcessShould
{
    [IntegrationFact]
    public async Task Run_child_processor()
    {
        string? connection = Environment.GetEnvironmentVariable("AVALON_CRASHES_CHILD_CONNECTION");
        if (connection is null) return;
        string root = Environment.GetEnvironmentVariable("AVALON_CRASHES_CHILD_ROOT")!;
        string stage = Environment.GetEnvironmentVariable("AVALON_CRASHES_CHILD_STAGE")!;
        var clock = new RecoveryClock();
        var factory = new CrashesDbContextFactory(new() { ConnectionString = connection });
        var queue = new CrashQueue(factory, clock);
        CrashLease lease = (await queue.ClaimAsync(clock.Now, default))!;
        var wrapped = new RecoveryQueue(queue, stage, root);
        var pipeline = new RecoveryPipeline(stage, root);
        await new CrashProcessor(wrapped, new RecoveryObjects(lease.Source), new(new() { ScratchRoot = Path.Combine(root, "scratch") }), pipeline, new(), new(), clock)
            .ProcessAsync(lease, default);
    }
}

internal sealed class RecoveryClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

internal static class RecoveryData
{
    public static StoredCrashObject Source => new("1.0.0/2026-10-24/11111111111111111111111111111111.zip", "etag", 10, new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));
    public static CrashAnalysis Analysis() => new(1, new("EXCEPTION_ACCESS_VIOLATION_READ", 0xc0000005, "0x0000000000001010", 0, 1),
        new("Windows NT", "10", "19045", "amd64", "cpu"),
        [new(0, 1, [new("runtime.exe", "0x0000000000000010", "Game::Tick", null, null, [], "context") { ImageKey = "12341000" }])],
        [new("runtime.exe", "12341000", "runtime.pdb", "ABCD1", "1.0.0", "0x0000000000001000", "0x0000000000001000", true)],
        new(1, "runtime", "1.0.0", "abc", 0xc0000005, "1010", 1, Source.LastModified), null, CrashQualityFlags.None, new("0.27.0", "2.3.9", "v1"));
    public static CrashAttemptResult Result() { CrashAnalysis analysis = Analysis(); return CrashAttemptResult.Processed(analysis, CrashSignature.Create(analysis)); }
}

internal sealed class RecoveryPipeline(string? stage = null, string? root = null) : ICrashAnalysisPipeline
{
    public async Task<CrashAnalysis> AnalyzeAsync(StoredCrashObject source, CrashAttemptWorkspace workspace, CancellationToken ct)
    {
        await workspace.WriteAsync("dump.dmp", new byte[] { 1 }, ct);
        if (stage == "holding-lease")
        {
            await File.WriteAllTextAsync(Path.Combine(root!, "ready"), "ready", ct);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
        return RecoveryData.Analysis();
    }
}

internal sealed class RecoveryObjects(StoredCrashObject source) : ICrashObjectStore
{
    public Task<StoredCrashObject?> HeadAsync(string key, CancellationToken ct) => Task.FromResult<StoredCrashObject?>(source);
    public Task DownloadAsync(StoredCrashObject expected, string destination, long maxBytes, CancellationToken ct) => throw new NotSupportedException();
    public Task<CrashObjectPage> ListAsync(string prefix, string? delimiter, string? continuationToken, CancellationToken ct) => throw new NotSupportedException();
}

internal sealed class RecoveryQueue(ICrashQueue inner, string stage, string root) : ICrashQueue
{
    public int Completions { get; private set; }
    public async Task<bool> CompleteAsync(CrashLease lease, CrashAttemptResult result, DateTimeOffset now, CancellationToken ct)
    {
        Completions++;
        if (stage == "before-completion")
        {
            await File.WriteAllTextAsync(Path.Combine(root, "ready"), "ready", ct);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
        bool accepted = await inner.CompleteAsync(lease, result, now, ct);
        if (stage == "uncertain" && Completions == 1) throw new TimeoutException();
        if (stage == "after-commit")
        {
            await File.WriteAllTextAsync(Path.Combine(root, "ready"), "ready", ct);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
        return accepted;
    }
    public Task<bool> RenewAsync(CrashLease lease, DateTimeOffset now, CancellationToken ct) => inner.RenewAsync(lease, now, ct);
    public Task<CrashLease?> ClaimAsync(DateTimeOffset now, CancellationToken ct) => inner.ClaimAsync(now, ct);
    public Task<bool> DiscoverAsync(StoredCrashObject source, DateTimeOffset now, CancellationToken ct) => inner.DiscoverAsync(source, now, ct);
}
