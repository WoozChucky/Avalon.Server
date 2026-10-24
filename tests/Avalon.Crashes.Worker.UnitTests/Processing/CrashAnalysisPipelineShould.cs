using System.Security.Cryptography;
using System.Text;
using Avalon.Crashes.UnitTests;
using Avalon.Crashes.Worker.Processing;
using Avalon.Crashes.Worker.Storage;
using Avalon.Crashes.Worker.Symbols;
using Avalon.Crashes.Worker.UnitTests.Symbols;
using Xunit;

namespace Avalon.Crashes.Worker.UnitTests.Processing;

public sealed class CrashAnalysisPipelineShould
{
    [Fact]
    public async Task Should_validate_resolve_walk_and_project_with_missing_public_symbols()
    {
        await using var fixture = new Fixture();
        CrashAnalysis analysis = await fixture.RunAsync();
        Assert.True(analysis.Quality.HasFlag(CrashQualityFlags.MissingSystemSymbols));
        Assert.Equal("Game::Tick", analysis.Threads[0].Frames[0].Function);
        Assert.Equal(fixture.Objects.Bytes.LongLength, fixture.Store.InitialBudget);
        Assert.Equal(CrashToolKind.Stackwalk, fixture.Runner.Invocation!.Kind);
        Assert.Equal(TimeSpan.FromSeconds(60), fixture.Runner.Invocation.WallTimeout);
        Assert.Equal(45, fixture.Runner.Invocation.CpuSeconds);
        Assert.Equal(16L * 1024 * 1024, fixture.Runner.Invocation.StdoutLimitBytes);
        Assert.Equal(64 * 1024, fixture.Runner.Invocation.StderrLimitBytes);
    }

    [Fact]
    public async Task Should_charge_the_archive_and_symbols_to_one_attempt_budget()
    {
        await using var fixture = new Fixture(downloadBytes: CrashFixture.Archive(CrashFixture.TwoModuleDump()).Length + 1);
        CrashSymbolException error = await Assert.ThrowsAsync<CrashSymbolException>(() => fixture.RunAsync());
        Assert.Equal(CrashFailureCode.ResourceLimit, error.Code);
        Assert.Equal(fixture.Objects.Bytes.LongLength, fixture.Store.InitialBudget);
        Assert.Null(fixture.Runner.Invocation);
    }

    [Fact]
    public async Task Should_reserve_expanded_dump_bytes_before_extracting()
    {
        await using var fixture = new Fixture(scratchBytes: CrashLimits.ArchiveBytes);
        await Assert.ThrowsAsync<CrashScratchLimitException>(() => fixture.RunAsync());
        Assert.Null(fixture.Runner.Invocation);
        Assert.Empty(Directory.GetDirectories(fixture.Scratch));
    }

    [Fact]
    public async Task Should_keep_missing_required_symbols_retryable_after_real_archive_validation()
    {
        await using var fixture = new Fixture();
        fixture.Store.MissingRuntime = true;
        CrashSymbolException error = await Assert.ThrowsAsync<CrashSymbolException>(() => fixture.RunAsync());
        Assert.Equal(CrashFailureCode.MissingRuntimeSymbols, error.Code);
        Assert.Null(fixture.Runner.Invocation);
    }

    [Theory]
    [InlineData(ToolFailureKind.OutputLimit, CrashFailureCode.ResourceLimit)]
    [InlineData(ToolFailureKind.WallTime, CrashFailureCode.ToolTimeout)]
    [InlineData(ToolFailureKind.ToolIdentity, CrashFailureCode.ConfigurationInvalid)]
    public async Task Should_map_native_failures_without_reusing_the_disposed_main_attempt(ToolFailureKind failure, CrashFailureCode code)
    {
        await using var fixture = new Fixture();
        fixture.Runner.Failure = failure;
        CrashSymbolException error = await Assert.ThrowsAsync<CrashSymbolException>(() => fixture.RunAsync());
        Assert.Equal(code, error.Code);
        Assert.Empty(Directory.GetDirectories(fixture.Scratch));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "pipeline-" + Guid.NewGuid().ToString("N"));
        public string Scratch => Path.Combine(_root, "scratch");
        public Objects Objects { get; } = new();
        public Store Store { get; } = new();
        public Runner Runner { get; } = new();
        private readonly CrashWorkspace _workspaces;
        private readonly CrashAnalysisPipeline _pipeline;
        private readonly PublicSymbolClient _public;
        public Fixture(long downloadBytes = 1024 * 1024, long scratchBytes = 2L * 1024 * 1024 * 1024)
        {
            _workspaces = new(new() { ScratchRoot = Scratch, ScratchLimitBytes = scratchBytes });
            var options = new CrashSymbolOptions { CacheRoot = Path.Combine(_root, "cache"), DownloadBytes = downloadBytes, PublicEnabled = false };
            _public = new(new CrashSymbolResolverShould.Handler(), options);
            var resolver = new CrashSymbolResolver(options, Store, new(options), _public, new CrashSymbolResolverShould.Lock(), Runner, _workspaces);
            _pipeline = new(Objects, new(), resolver, Runner, options);
        }
        public async Task<CrashAnalysis> RunAsync()
        {
            await using CrashAttemptWorkspace attempt = await _workspaces.CreateAsync(Guid.NewGuid(), default);
            return await _pipeline.AnalyzeAsync(new($"{CrashFixture.Version}/2026-10-24/11111111111111111111111111111111.zip", "etag", Objects.Bytes.Length, DateTimeOffset.UtcNow), attempt, default);
        }
        public ValueTask DisposeAsync()
        {
            _public.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Objects : ICrashObjectStore
    {
        public byte[] Bytes { get; } = CrashFixture.Archive(CrashFixture.TwoModuleDump());
        public async Task DownloadAsync(StoredCrashObject expected, string destination, long maxBytes, CancellationToken ct)
        {
            Assert.True(Bytes.Length <= maxBytes);
            Assert.False(File.Exists(destination));
            await File.WriteAllBytesAsync(destination, Bytes, ct);
        }
        public Task<StoredCrashObject?> HeadAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task<CrashObjectPage> ListAsync(string prefix, string? delimiter, string? continuationToken, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Store : ICrashSymbolStore
    {
        public long InitialBudget { get; private set; }
        public bool MissingRuntime { get; set; }
        public async Task<SymbolCacheMetadata?> DownloadConvertedAsync(ModuleIdentity identity, string cacheVersion, string destination, long maxBytes, SymbolDownloadBudget budget, CancellationToken ct)
        {
            InitialBudget = budget.UsedBytes;
            if (MissingRuntime) { await File.WriteAllBytesAsync(destination, [], ct); return null; }
            byte[] content = Encoding.UTF8.GetBytes(SymbolCacheShould.Content(identity));
            budget.Charge(content.Length);
            await File.WriteAllBytesAsync(destination, content, ct);
            return new(identity.Name, identity.ImageKey, identity.PdbName!, identity.PdbKey!, cacheVersion,
                CrashSymbolOptions.ConverterVersion, Convert.ToHexString(SHA256.HashData(content)), content.Length);
        }
        public Task DownloadOriginalAsync(ModuleIdentity identity, bool pdb, string destination, long maxBytes, SymbolDownloadBudget budget, CancellationToken ct)
            => throw new CrashSymbolException(CrashFailureCode.MissingRuntimeSymbols);
        public Task UploadConvertedAsync(ModuleIdentity identity, SymbolCacheMetadata metadata, string source, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Runner : ICrashToolRunner
    {
        public ToolInvocation? Invocation { get; private set; }
        public ToolFailureKind Failure { get; set; }
        public async Task<ToolRunResult> RunAsync(ToolInvocation invocation, CancellationToken ct)
        {
            Invocation = invocation;
            Assert.True(File.Exists(invocation.Arguments[1]));
            Assert.StartsWith(invocation.Workspace.DirectoryPath, invocation.Arguments[2], StringComparison.Ordinal);
            if (Failure != ToolFailureKind.None)
            {
                await invocation.Workspace.DisposeAsync();
                return new(null, null, null, Failure);
            }
            string path = await invocation.Workspace.WriteAsync("stack.json", Encoding.UTF8.GetBytes("""
                {"crash_info":{"type":"EXCEPTION_ACCESS_VIOLATION_READ","address":"0x12340010","crashing_thread":0},
                "system_info":{"os":"Windows NT","cpu_arch":"amd64"},
                "threads":[{"thread_id":42,"frames":[{"function":"Game::Tick","trust":"context","offset":"0x12340010","module":"runtime.exe"}]}],"modules":[]}
                """), ct);
            return new(0, path, null, ToolFailureKind.None);
        }
    }
}
