using System.Text.Json;
using Avalon.Crashes.Persistence;
using Avalon.Crashes.Validation;
using Avalon.Crashes.Worker.Processing;
using Avalon.Crashes.Worker.Symbols;
using Xunit;
using Xunit.Abstractions;

namespace Avalon.Crashes.IntegrationTests.Processing;

[Trait("Category", "CrashesNative")]
public sealed class CrashSymbolResolverNativeShould(ITestOutputHelper output)
{
    [IntegrationFact]
    public async Task Should_convert_private_release_pair_through_resolver_and_recover_source_with_cfi()
    {
        Assert.True(OperatingSystem.IsLinux(), "Native symbol gate requires Linux and protected fixture mounts.");
        string pair = Environment.GetEnvironmentVariable("AVALON_CRASHES_PRIVATE_PAIR") ?? throw new InvalidOperationException("Private pair path required.");
        string fixture = Environment.GetEnvironmentVariable("AVALON_CRASHES_PRIVATE_FIXTURE") ?? throw new InvalidOperationException("Private fixture path required.");
        string root = Path.Combine(Path.GetTempPath(), "resolver-native-" + Guid.NewGuid().ToString("N"));
        var toolOptions = new CrashToolOptions { ScratchRoot = Path.Combine(root, "scratch") };
        var options = new CrashSymbolOptions { CacheRoot = Path.Combine(root, "cache"), PublicEnabled = false };
        var workspaces = new CrashWorkspace(toolOptions);
        var runner = new CrashToolRunner(toolOptions);
        Assert.Equal(ToolFailureKind.None, await runner.ValidateToolsAsync(default));
        using var publicClient = new PublicSymbolClient(options);
        var resolver = new CrashSymbolResolver(options, new PrivatePairStore(pair), new(options), publicClient, new LocalLock(), runner, workspaces);
        try
        {
            await using CrashAttemptWorkspace attempt = await workspaces.CreateAsync(Guid.NewGuid(), default);
            string dump = await attempt.WriteAsync("dump.dmp", await File.ReadAllBytesAsync(Path.Combine(fixture, "dump.dmp")), default);
            DumpIdentity identity = new MinidumpIdentityReader().Read(await File.ReadAllBytesAsync(dump));
            var input = new CrashInput(dump, new(1, "runtime.exe", "0.4.0", "fixture", 0xc0000005, "0x0", 23952, DateTimeOffset.UnixEpoch), null, identity.Runtime, CrashQualityFlags.None)
            { Modules = identity.Modules };
            var budget = new SymbolDownloadBudget();
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(8));
            SymbolResolution result = await resolver.ResolveAsync(input, attempt, budget, deadline.Token);
            string symbol = Path.Combine(result.BreakpadRoot, identity.Runtime.PdbName, identity.Runtime.PdbKey, "runtime.sym");
            Assert.True(await SymbolIdentityValidator.ValidateConvertedAsync(identity.Modules.Single(m => m.Name == "runtime.exe"), symbol, deadline.Token));
            ToolRunResult walked = await runner.RunAsync(new(CrashToolKind.Stackwalk, ["--json", dump, result.BreakpadRoot], attempt,
                TimeSpan.FromSeconds(60), 45, 16 * 1024 * 1024, 64 * 1024), deadline.Token);
            Assert.Equal(ToolFailureKind.None, walked.Failure);
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(walked.OutputPath!, deadline.Token));
            JsonElement crash = document.RootElement.GetProperty("crash_info");
            Assert.Equal("EXCEPTION_ACCESS_VIOLATION_WRITE", crash.GetProperty("type").GetString());
            int thread = crash.GetProperty("crashing_thread").GetInt32();
            JsonElement.ArrayEnumerator frames = document.RootElement.GetProperty("threads")[thread].GetProperty("frames").EnumerateArray();
            foreach (string name in new[] { "Application.cpp", "main.cpp" })
            {
                JsonElement frame = frames.First(f => f.TryGetProperty("file", out JsonElement file) && file.GetString() == name);
                Assert.True(frame.GetProperty("line").GetInt32() > 0);
                Assert.Equal("cfi", frame.GetProperty("trust").GetString());
                output.WriteLine($"{name}:{frame.GetProperty("line").GetInt32()} trust=cfi");
            }
            Assert.Contains(frames, f => f.TryGetProperty("inlines", out JsonElement inlines) && inlines.ValueKind == JsonValueKind.Array && inlines.GetArrayLength() > 0);
            output.WriteLine($"debugId={identity.Runtime.PdbKey}; imageId={identity.Runtime.ImageKey}; downloaded={budget.UsedBytes}; symbols={new FileInfo(symbol).Length}");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class LocalLock : ICrashSymbolLock, IAsyncDisposable
    {
        public Task<IAsyncDisposable> AcquireAsync(string cacheVersion, string moduleIdentity, CancellationToken ct) => Task.FromResult<IAsyncDisposable>(this);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class PrivatePairStore(string pair) : ICrashSymbolStore
    {
        public async Task DownloadOriginalAsync(ModuleIdentity identity, bool pdb, string destination, long maxBytes, SymbolDownloadBudget budget, CancellationToken ct)
        {
            string source = Path.Combine(pair, pdb ? identity.PdbName! : identity.Name);
            Assert.True(new FileInfo(source).Length <= maxBytes);
            await using FileStream input = File.OpenRead(source);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
            byte[] buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) != 0)
            {
                total += read;
                Assert.True(total <= maxBytes);
                budget.Charge(read);
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }
        public async Task<SymbolCacheMetadata?> DownloadConvertedAsync(ModuleIdentity identity, string cacheVersion, string destination, long maxBytes, SymbolDownloadBudget budget, CancellationToken ct)
        {
            await File.WriteAllBytesAsync(destination, [], ct);
            return null;
        }
        public Task UploadConvertedAsync(ModuleIdentity identity, SymbolCacheMetadata metadata, string source, CancellationToken ct) => Task.CompletedTask;
    }
}
