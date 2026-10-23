using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Avalon.Crashes.Persistence;
using Avalon.Crashes.Worker.Processing;
using Avalon.Crashes.Worker.Symbols;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Avalon.Crashes.Worker.UnitTests.Symbols;

public sealed class CrashSymbolResolverShould
{
    [Fact]
    public async Task Should_preserve_runtime_success_when_optional_database_lock_fails()
    {
        await using var fixture = new Fixture();
        fixture.SymbolLock.FailOptional = true;
        ModuleIdentity system = SymbolIdentityValidatorShould.s_runtime with { Name = "ntdll.dll", PdbName = "ntdll.pdb", PdbKey = "112233445566778899AABBCCDDEEFF001" };
        await using CrashAttemptWorkspace attempt = await fixture.Workspaces.CreateAsync(Guid.NewGuid(), default);
        SymbolResolution result = await fixture.Resolver.ResolveAsync(Input("dump.dmp") with { Modules = [SymbolIdentityValidatorShould.s_runtime, system] }, attempt, new(), default);
        Assert.Single(result.AvailableModules);
        Assert.Equal(CrashQualityFlags.MissingSystemSymbols, result.Quality);
    }

    [Fact]
    public async Task Should_limit_public_work_to_sixteen_modules_and_never_disclose_runtime_debug_identity()
    {
        await using var fixture = new Fixture();
        ModuleIdentity runtime = SymbolIdentityValidatorShould.s_runtime;
        ModuleIdentity[] modules = Enumerable.Range(1, 20).Select(i => runtime with
        {
            Name = "ntdll.dll",
            PdbName = "ntdll.pdb",
            PdbKey = i.ToString("X32") + "1",
            BaseAddress = (ulong)i * 0x10000
        }).ToArray();
        await using CrashAttemptWorkspace attempt = await fixture.Workspaces.CreateAsync(Guid.NewGuid(), default);
        CrashInput input = Input("dump.dmp") with { Modules = [runtime, runtime with { Name = "ntdll.dll", PdbName = "ntdll.pdb" }, .. modules] };
        SymbolResolution result = await fixture.Resolver.ResolveAsync(input, attempt, new(), default);
        Assert.Equal(16, fixture.Handler.Calls);
        Assert.Single(result.AvailableModules);
        Assert.Equal(CrashQualityFlags.MissingSystemSymbols, result.Quality);
    }

    [Fact]
    public async Task Should_cover_public_cache_lock_and_conversion_in_one_sixty_second_phase()
    {
        var clock = new FakeTimeProvider();
        await using var fixture = new Fixture(clock: clock);
        ModuleIdentity system = SymbolIdentityValidatorShould.s_runtime with { Name = "ntdll.dll", PdbName = "ntdll.pdb", PdbKey = "112233445566778899AABBCCDDEEFF001" };
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Store.BeforeConverted = async (module, ct) =>
        {
            if (module.Name != "runtime.exe")
            {
                waiting.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
        };
        await using CrashAttemptWorkspace attempt = await fixture.Workspaces.CreateAsync(Guid.NewGuid(), default);
        Task<SymbolResolution> resolving = fixture.Resolver.ResolveAsync(Input("dump.dmp") with { Modules = [SymbolIdentityValidatorShould.s_runtime, system] }, attempt, new(), default);
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.False(resolving.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(1));
        SymbolResolution result = await resolving.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Single(result.AvailableModules);
        Assert.Equal(CrashQualityFlags.MissingSystemSymbols, result.Quality);
    }

    [Fact]
    public async Task Should_use_local_success_when_cache_upload_fails()
    {
        await using var fixture = new Fixture();
        fixture.Store.FailUpload = true;
        await using CrashAttemptWorkspace attempt = await fixture.Workspaces.CreateAsync(Guid.NewGuid(), default);
        string dump = await attempt.WriteAsync("dump.dmp", new byte[] { 1, 2, 3 }, default);
        SymbolResolution result = await fixture.Resolver.ResolveAsync(Input(dump), attempt, new(), default);
        Assert.True(result.CacheUploadFailed);
        Assert.True(File.Exists(dump));
        Assert.True(File.Exists(Path.Combine(result.BreakpadRoot, "runtime.pdb", SymbolIdentityValidatorShould.s_runtime.PdbKey!, "runtime.sym")));
        Assert.Single(result.AvailableModules);
        Assert.Equal(1, fixture.Runner.Calls);
        Assert.DoesNotContain("private/home", await File.ReadAllTextAsync(Directory.GetFiles(result.BreakpadRoot, "*.sym", SearchOption.AllDirectories).Single()));
        fixture.Store.FailUpload = false;
        SymbolResolution retry = await fixture.Resolver.ResolveAsync(Input(dump), attempt, new(), default);
        Assert.False(retry.CacheUploadFailed);
        Assert.Single(retry.AvailableModules);
        Assert.Equal(1, fixture.Runner.Calls);
        Assert.Equal(2, fixture.Store.Uploads);
    }

    [Fact]
    public async Task Should_rebuild_corrupt_converted_symbols()
    {
        await using var fixture = new Fixture();
        fixture.Store.Remote = "MODULE wrong";
        await using CrashAttemptWorkspace attempt = await fixture.Workspaces.CreateAsync(Guid.NewGuid(), default);
        SymbolResolution result = await fixture.Resolver.ResolveAsync(Input("dump.dmp"), attempt, new(), default);
        Assert.Single(result.AvailableModules);
        Assert.Equal(1, fixture.Runner.Calls);
    }

    [Fact]
    public async Task Should_keep_report_and_runtime_symbols_when_optional_conversion_destroys_its_workspace()
    {
        await using var fixture = new Fixture();
        ModuleIdentity system = SymbolIdentityValidatorShould.s_runtime with { Name = "ntdll.dll", PdbName = "ntdll.pdb", PdbKey = "112233445566778899AABBCCDDEEFF001" };
        fixture.Handler.Response = request => new(HttpStatusCode.OK) { Content = new ByteArrayContent(request.RequestUri!.AbsolutePath.EndsWith(".pdb", StringComparison.Ordinal) ? Pdb(system) : Pe(system)) };
        fixture.Runner.FailOptional = true;
        await using CrashAttemptWorkspace attempt = await fixture.Workspaces.CreateAsync(Guid.NewGuid(), default);
        string dump = await attempt.WriteAsync("dump.dmp", new byte[] { 1 }, default);
        CrashInput input = Input(dump) with { Modules = [SymbolIdentityValidatorShould.s_runtime, system] };
        SymbolResolution result = await fixture.Resolver.ResolveAsync(input, attempt, new(), default);
        Assert.True(File.Exists(dump));
        Assert.Equal(CrashQualityFlags.MissingSystemSymbols, result.Quality);
        Assert.Single(result.AvailableModules);
        Assert.Equal(2, fixture.Runner.Calls);
        Assert.Single(Directory.GetFiles(result.BreakpadRoot, "*.sym", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Should_require_matching_pe_and_pdb_before_conversion()
    {
        await using var fixture = new Fixture();
        fixture.Store.Mismatch = true;
        await using CrashAttemptWorkspace attempt = await fixture.Workspaces.CreateAsync(Guid.NewGuid(), default);
        CrashSymbolException error = await Assert.ThrowsAsync<CrashSymbolException>(() => fixture.Resolver.ResolveAsync(Input("dump.dmp"), attempt, new(), default));
        Assert.Equal(CrashFailureCode.MissingRuntimeSymbols, error.Code);
        Assert.Equal(0, fixture.Runner.Calls);
    }

    [Fact]
    public async Task Should_classify_invalid_converter_output_as_a_tool_failure()
    {
        await using var fixture = new Fixture();
        fixture.Runner.InvalidOutput = true;
        await using CrashAttemptWorkspace attempt = await fixture.Workspaces.CreateAsync(Guid.NewGuid(), default);
        CrashSymbolException error = await Assert.ThrowsAsync<CrashSymbolException>(() => fixture.Resolver.ResolveAsync(Input("dump.dmp"), attempt, new(), default));
        Assert.Equal(CrashFailureCode.ToolFailed, error.Code);
        Assert.Empty(Directory.GetFiles(fixture.CacheRoot, "*.sym", SearchOption.AllDirectories));
    }

    internal static CrashInput Input(string dump) => new(dump, new(1, "runtime.exe", "1.0", "git", 0, "0x0", 1, DateTimeOffset.UnixEpoch), null,
        new("runtime.exe", SymbolIdentityValidatorShould.s_runtime.ImageKey, "runtime.pdb", SymbolIdentityValidatorShould.s_runtime.PdbKey!), CrashQualityFlags.None)
    { Modules = [SymbolIdentityValidatorShould.s_runtime] };

    internal sealed class Fixture : IAsyncDisposable
    {
        public CrashWorkspace Workspaces { get; } = PublicSymbolClientShould.Workspace();
        public Store Store { get; } = new();
        public Runner Runner { get; } = new();
        public Handler Handler { get; } = new();
        public Lock SymbolLock { get; } = new();
        public string CacheRoot { get; } = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public CrashSymbolResolver Resolver { get; }
        private readonly PublicSymbolClient _public;
        public Fixture(TimeProvider? clock = null)
        {
            var options = new CrashSymbolOptions { CacheRoot = CacheRoot };
            _public = new(Handler, options, clock);
            Resolver = new(options, Store, new(options, clock), _public, SymbolLock, Runner, Workspaces, clock);
        }
        public ValueTask DisposeAsync()
        {
            _public.Dispose();
            if (Directory.Exists(CacheRoot)) Directory.Delete(CacheRoot, true);
            return ValueTask.CompletedTask;
        }
    }

    internal sealed class Handler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Func<HttpRequestMessage, HttpResponseMessage> Response { get; set; } = _ => new(HttpStatusCode.NotFound);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Response(request));
        }
    }

    internal sealed class Lock : ICrashSymbolLock, IAsyncDisposable
    {
        public bool FailOptional { get; set; }
        public Task<IAsyncDisposable> AcquireAsync(string cacheVersion, string moduleIdentity, CancellationToken ct) =>
            FailOptional && moduleIdentity.Contains("ntdll.dll", StringComparison.Ordinal)
                ? Task.FromException<IAsyncDisposable>(new TestDatabaseException()) : Task.FromResult<IAsyncDisposable>(this);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestDatabaseException : System.Data.Common.DbException;

    internal sealed class Store : ICrashSymbolStore
    {
        public bool FailUpload { get; set; }
        public int Uploads { get; private set; }
        public bool Mismatch { get; set; }
        public string? Remote { get; set; }
        public Func<ModuleIdentity, CancellationToken, Task>? BeforeConverted { get; set; }
        public async Task DownloadOriginalAsync(ModuleIdentity identity, bool pdb, string destination, long maxBytes, SymbolDownloadBudget budget, CancellationToken ct)
        {
            byte[] bytes = pdb ? Pdb(Mismatch ? identity with { PdbKey = "112233445566778899AABBCCDDEEFF001" } : identity) : Pe(identity);
            Assert.True(bytes.Length <= maxBytes);
            budget.Charge(bytes.Length);
            await File.WriteAllBytesAsync(destination, bytes, ct);
        }
        public async Task<SymbolCacheMetadata?> DownloadConvertedAsync(ModuleIdentity identity, string cacheVersion, string destination, long maxBytes, SymbolDownloadBudget budget, CancellationToken ct)
        {
            if (BeforeConverted is not null)
                await BeforeConverted(identity, ct);
            await File.WriteAllTextAsync(destination, Remote ?? "", ct);
            return Remote is null ? null : new(identity.Name, identity.ImageKey, identity.PdbName!, identity.PdbKey!, cacheVersion,
                CrashSymbolOptions.ConverterVersion, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Remote))), Encoding.UTF8.GetByteCount(Remote));
        }
        public Task UploadConvertedAsync(ModuleIdentity identity, SymbolCacheMetadata metadata, string source, CancellationToken ct)
        {
            Uploads++;
            return FailUpload ? Task.FromException(new CrashSymbolException(CrashFailureCode.StorageUnavailable)) : Task.CompletedTask;
        }
    }

    internal sealed class Runner : ICrashToolRunner
    {
        public int Calls { get; private set; }
        public bool FailOptional { get; set; }
        public bool InvalidOutput { get; set; }
        public async Task<ToolRunResult> RunAsync(ToolInvocation invocation, CancellationToken ct)
        {
            Calls++;
            Assert.Equal(new[] { "--inlines", "--check-cfi" }, invocation.Arguments.Take(2));
            if (FailOptional && Calls > 1)
            {
                await invocation.Workspace.DisposeAsync();
                return new(null, null, null, ToolFailureKind.WallTime);
            }
            string path = await invocation.Workspace.WriteAsync("converted.sym", Encoding.UTF8.GetBytes(InvalidOutput ? "bad" : SymbolCacheShould.Content(SymbolIdentityValidatorShould.s_runtime)), ct);
            return new(0, path, null, ToolFailureKind.None);
        }
    }

    internal static byte[] Pe(ModuleIdentity identity)
    {
        byte[] data = new byte[2048];
        void U16(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), value);
        void U32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
        U16(0, 0x5a4d); U32(60, 128); U32(128, 0x4550); U16(132, 0x8664); U16(134, 1);
        U32(136, Convert.ToUInt32(identity.ImageKey[..8], 16)); U16(148, 240); U16(150, 2);
        const int Optional = 152;
        U16(Optional, 0x20b); U32(Optional + 32, 4096); U32(Optional + 36, 512); U32(Optional + 56, Convert.ToUInt32(identity.ImageKey[8..], 16));
        U32(Optional + 60, 512); U32(Optional + 108, 16); U32(Optional + 160, 4096); U32(Optional + 164, 28);
        int section = Optional + 240;
        Encoding.ASCII.GetBytes(".rdata").CopyTo(data, section); U32(section + 8, 1024); U32(section + 12, 4096); U32(section + 16, 1024); U32(section + 20, 512);
        byte[] cv = Encoding.UTF8.GetBytes(identity.PdbName + "\0");
        U32(512 + 12, 2); U32(512 + 16, (uint)(24 + cv.Length)); U32(512 + 20, 4608); U32(512 + 24, 1024);
        "RSDS"u8.CopyTo(data.AsSpan(1024)); Guid.ParseExact(identity.PdbKey![..32], "N").ToByteArray().CopyTo(data, 1028);
        U32(1044, Convert.ToUInt32(identity.PdbKey[32..], 16)); cv.CopyTo(data, 1048);
        return data;
    }

    internal static byte[] Pdb(ModuleIdentity identity)
    {
        byte[] data = new byte[2048];
        void U32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
        "Microsoft C/C++ MSF 7.00\r\n\x1a\x44\x53\0\0\0"u8.CopyTo(data);
        U32(32, 512); U32(40, 4); U32(44, 16); U32(52, 1); U32(512, 2);
        U32(1024, 2); U32(1028, 0); U32(1032, 28); U32(1036, 3);
        U32(1544, Convert.ToUInt32(identity.PdbKey![32..], 16)); Guid.ParseExact(identity.PdbKey[..32], "N").ToByteArray().CopyTo(data, 1548);
        return data;
    }
}
