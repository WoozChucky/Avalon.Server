using Avalon.Crashes.Worker.Processing;
using Avalon.Crashes.Worker.Symbols;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Avalon.Crashes.Worker.UnitTests.Symbols;

public sealed class SymbolCacheShould
{
    internal static string Content(ModuleIdentity module) => $"MODULE windows x86_64 {module.PdbKey} {module.PdbName}\nFILE 0 C:/private/home/source.cpp\nFUNC 1 1 0 function\nSTACK CFI INIT 1 1 .cfa: $rsp 8 +\n";

    [Fact]
    public async Task Should_reject_a_cache_root_overlapping_report_scratch()
    {
        await using CrashAttemptWorkspace attempt = await PublicSymbolClientShould.Workspace().CreateAsync(Guid.NewGuid(), default);
        var cache = new SymbolCache(new() { CacheRoot = attempt.DirectoryPath });
        await Assert.ThrowsAsync<CrashSymbolException>(() => cache.MaterializeAsync(SymbolIdentityValidatorShould.s_runtime, attempt, default));
    }

    [Fact]
    public async Task Should_trim_existing_cache_before_use()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "interrupted.tmp"), new byte[128]);
            var cache = new SymbolCache(new() { CacheRoot = root, LocalCacheBytes = 64 });
            await using CrashAttemptWorkspace attempt = await PublicSymbolClientShould.Workspace().CreateAsync(Guid.NewGuid(), default);
            await cache.MaterializeAsync(SymbolIdentityValidatorShould.s_runtime, attempt, default);
            Assert.True(Directory.GetFiles(root).Sum(p => new FileInfo(p).Length) <= 64);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Should_evict_the_least_recently_used_complete_entry()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string source = Path.GetTempFileName();
        try
        {
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var cache = new SymbolCache(new() { CacheRoot = root, LocalCacheBytes = 1300 }, clock);
            ModuleIdentity first = SymbolIdentityValidatorShould.s_runtime;
            ModuleIdentity second = first with { PdbKey = "112233445566778899AABBCCDDEEFF001" };
            ModuleIdentity third = first with { PdbKey = "2233445566778899AABBCCDDEEFF00111" };
            foreach (ModuleIdentity module in new[] { first, second })
            {
                await File.WriteAllTextAsync(source, Content(module));
                await cache.PublishAsync(module, source, default);
                clock.Advance(TimeSpan.FromSeconds(1));
            }
            await using CrashAttemptWorkspace attempt = await PublicSymbolClientShould.Workspace().CreateAsync(Guid.NewGuid(), default);
            Assert.NotNull(await cache.MaterializeAsync(first, attempt, default));
            clock.Advance(TimeSpan.FromSeconds(1));
            await File.WriteAllTextAsync(source, Content(third));
            await cache.PublishAsync(third, source, default);
            Assert.NotNull(await cache.MaterializeAsync(first, attempt, default));
            Assert.Null(await cache.MaterializeAsync(second, attempt, default));
            Assert.NotNull(await cache.MaterializeAsync(third, attempt, default));
        }
        finally { File.Delete(source); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Should_materialize_verified_symbols_and_rebuild_corrupt_converted_symbols()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string source = Path.GetTempFileName();
        try
        {
            ModuleIdentity module = SymbolIdentityValidatorShould.s_runtime;
            await File.WriteAllTextAsync(source, Content(module));
            var cache = new SymbolCache(new() { CacheRoot = root });
            await cache.PublishAsync(module, source, default);
            await using CrashAttemptWorkspace attempt = await PublicSymbolClientShould.Workspace().CreateAsync(Guid.NewGuid(), default);
            string? result = await cache.MaterializeAsync(module, attempt, default);
            Assert.NotNull(result);
            Assert.Contains("FUNC ", await File.ReadAllTextAsync(result));
            string cached = Directory.GetFiles(root, "*.sym", SearchOption.AllDirectories).Single();
            await File.WriteAllTextAsync(cached, "corrupt");
            Assert.Null(await cache.MaterializeAsync(module, attempt, default));
            await cache.PublishAsync(module, source, default);
            Assert.NotNull(await cache.MaterializeAsync(module, attempt, default));
        }
        finally { File.Delete(source); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Should_bound_cache_with_lru_eviction_and_count_metadata()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string source = Path.GetTempFileName();
        try
        {
            var options = new CrashSymbolOptions { CacheRoot = root, LocalCacheBytes = 850 };
            var cache = new SymbolCache(options);
            ModuleIdentity first = SymbolIdentityValidatorShould.s_runtime;
            ModuleIdentity second = first with { PdbKey = "112233445566778899AABBCCDDEEFF001" };
            foreach (ModuleIdentity module in new[] { first, second })
            {
                await File.WriteAllTextAsync(source, Content(module));
                await cache.PublishAsync(module, source, default);
            }
            Assert.True(Directory.GetFiles(root, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length) <= options.LocalCacheBytes);
            await using CrashAttemptWorkspace attempt = await PublicSymbolClientShould.Workspace().CreateAsync(Guid.NewGuid(), default);
            Assert.Null(await cache.MaterializeAsync(first, attempt, default));
            Assert.NotNull(await cache.MaterializeAsync(second, attempt, default));
        }
        finally { File.Delete(source); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Should_reject_path_escape_and_full_image_identity_mismatch()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string source = Path.GetTempFileName();
        try
        {
            var cache = new SymbolCache(new() { CacheRoot = root });
            ModuleIdentity module = SymbolIdentityValidatorShould.s_runtime;
            await File.WriteAllTextAsync(source, Content(module));
            await Assert.ThrowsAsync<CrashSymbolException>(() => cache.PublishAsync(module with { PdbName = "../runtime.pdb" }, source, default));
            await cache.PublishAsync(module, source, default);
            await using CrashAttemptWorkspace attempt = await PublicSymbolClientShould.Workspace().CreateAsync(Guid.NewGuid(), default);
            Assert.Null(await cache.MaterializeAsync(module with { ImageKey = "111111111000" }, attempt, default));
        }
        finally { File.Delete(source); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
