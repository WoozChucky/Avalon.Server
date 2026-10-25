using System.Reflection;
using Avalon.Crashes.Worker.Processing;
using Xunit;

namespace Avalon.Crashes.Worker.UnitTests.Processing;

public sealed class CrashWorkspaceShould
{
    [Fact]
    public async Task Isolate_attempts_and_remove_all_files_on_dispose()
    {
        using var root = new TestRoot();
        var manager = new CrashWorkspace(new() { ScratchRoot = root.Path });
        CrashReportId id = Guid.NewGuid();
        CrashAttemptWorkspace first = await manager.CreateAsync(id, default);
        await using CrashAttemptWorkspace second = await manager.CreateAsync(id, default);
        Assert.NotEqual(first.DirectoryPath, second.DirectoryPath);
        await first.WriteAsync("dump.dmp", new byte[10], default);
        await first.DisposeAsync();
        Assert.False(Directory.Exists(first.DirectoryPath));
        Assert.True(Directory.Exists(second.DirectoryPath));
    }

    [Fact]
    public async Task Reserve_aggregate_quota_before_creating_files()
    {
        using var root = new TestRoot();
        var manager = new CrashWorkspace(new() { ScratchRoot = root.Path, ScratchLimitBytes = 12 });
        await using CrashAttemptWorkspace first = await manager.CreateAsync(Guid.NewGuid(), default);
        await using CrashAttemptWorkspace second = await manager.CreateAsync(Guid.NewGuid(), default);
        await first.WriteAsync("one", new byte[8], default);
        await Assert.ThrowsAsync<CrashScratchLimitException>(() => second.WriteAsync("two", new byte[5], default));
        Assert.False(File.Exists(Path.Combine(second.DirectoryPath, "two")));
        await first.DisposeAsync();
        await second.WriteAsync("two", new byte[12], default);
    }

    [Fact]
    public async Task Count_writer_completing_between_measurement_and_competing_reservation()
    {
        using var root = new TestRoot();
        using var resumeScan = new ManualResetEventSlim();
        var scanPaused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completeWriter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool pauseNextScan = false;
        Func<string, long> measure = directory =>
        {
            long measuredBytes = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length);
            if (pauseNextScan)
            {
                scanPaused.SetResult();
                Assert.True(resumeScan.Wait(TimeSpan.FromSeconds(10)), "Competing scan was not resumed.");
            }
            return measuredBytes;
        };
        ConstructorInfo constructor = typeof(CrashWorkspace).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, [typeof(CrashToolOptions), typeof(Func<string, long>)], modifiers: null)!;
        var manager = (CrashWorkspace)constructor.Invoke([new CrashToolOptions { ScratchRoot = root.Path, ScratchLimitBytes = 12 }, measure]);
        await using CrashAttemptWorkspace writing = await manager.CreateAsync(Guid.NewGuid(), default);
        await using CrashAttemptWorkspace competing = await manager.CreateAsync(Guid.NewGuid(), default);
        Task<string> writer = writing.WriteExternalAsync("completing", 8, async (destination, _, ct) =>
        {
            await completeWriter.Task.WaitAsync(ct);
            await File.WriteAllBytesAsync(destination, new byte[8], ct);
        }, default);

        pauseNextScan = true;
        Task<string> competitor = Task.Run(() => competing.WriteAsync("competing", new byte[8], default));
        try
        {
            await scanPaused.Task.WaitAsync(TimeSpan.FromSeconds(10));
            completeWriter.SetResult();
            string completedPath = await writer.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(8, new FileInfo(completedPath).Length);
        }
        finally
        {
            completeWriter.TrySetResult();
            resumeScan.Set();
        }

        await Assert.ThrowsAsync<CrashScratchLimitException>(() => competitor);
        Assert.False(File.Exists(Path.Combine(competing.DirectoryPath, "competing")));
        Assert.Equal(8, Directory.EnumerateFiles(root.Path, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length));
    }

    [Fact]
    public async Task Bound_stream_writes_and_reject_path_traversal()
    {
        using var root = new TestRoot();
        var manager = new CrashWorkspace(new() { ScratchRoot = root.Path, ScratchLimitBytes = 20 });
        await using CrashAttemptWorkspace attempt = await manager.CreateAsync(Guid.NewGuid(), default);
        await Assert.ThrowsAsync<ArgumentException>(() => attempt.WriteAsync("../outside", new byte[1], default));
        await using Stream stream = await attempt.CreateFileAsync("bounded", 4, default);
        await Assert.ThrowsAsync<CrashScratchLimitException>(async () => await stream.WriteAsync(new byte[5]));
        Assert.Equal(0, new FileInfo(Path.Combine(attempt.DirectoryPath, "bounded")).Length);
    }

    [Fact]
    public async Task Reserve_external_adapter_quota_without_precreating_destination()
    {
        using var root = new TestRoot();
        var manager = new CrashWorkspace(new() { ScratchRoot = root.Path, ScratchLimitBytes = 12 });
        await using CrashAttemptWorkspace attempt = await manager.CreateAsync(Guid.NewGuid(), default);
        string path = await attempt.WriteExternalAsync("download.zip", 8, async (destination, cap, ct) =>
        {
            Assert.False(File.Exists(destination));
            Assert.Equal(8, cap);
            await File.WriteAllBytesAsync(destination, new byte[6], ct);
        }, default);
        Assert.Equal(6, new FileInfo(path).Length);
        bool called = false;
        await Assert.ThrowsAsync<CrashScratchLimitException>(() => attempt.WriteExternalAsync("next.zip", 7, (_, _, _) =>
        {
            called = true;
            return Task.CompletedTask;
        }, default));
        Assert.False(called);
        await Assert.ThrowsAsync<IOException>(() => attempt.WriteExternalAsync("partial", 6, async (destination, _, ct) =>
        {
            await File.WriteAllBytesAsync(destination, new byte[3], ct);
            throw new IOException("bounded test adapter interrupted");
        }, default));
        Assert.False(File.Exists(Path.Combine(attempt.DirectoryPath, "partial")));
        await attempt.WriteAsync("final", new byte[6], default);
    }

    [Fact]
    public async Task Clean_only_verified_children_and_leave_outside_sentinel()
    {
        using var root = new TestRoot();
        string sibling = root.Path + "-outside";
        Directory.CreateDirectory(sibling);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(sibling, "sentinel"), "keep");
            var manager = new CrashWorkspace(new() { ScratchRoot = root.Path });
            CrashAttemptWorkspace stale = await manager.CreateAsync(Guid.NewGuid(), default);
            Directory.CreateDirectory(Path.Combine(root.Path, "unowned"));
            await manager.CleanupStaleAsync(default);
            Assert.False(Directory.Exists(stale.DirectoryPath));
            Assert.True(Directory.Exists(Path.Combine(root.Path, "unowned")));
            Assert.True(File.Exists(Path.Combine(sibling, "sentinel")));
        }
        finally
        {
            Directory.Delete(sibling, true);
        }
    }

    [Fact]
    public async Task Refuse_symlink_roots_and_never_follow_nested_links()
    {
        if (!OperatingSystem.IsLinux())
            return; // Linux gate exercises links without requiring Windows developer-mode privileges.
        using var root = new TestRoot();
        string outside = root.Path + "-outside";
        Directory.CreateDirectory(outside);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(outside, "sentinel"), "keep");
            var manager = new CrashWorkspace(new() { ScratchRoot = root.Path });
            CrashAttemptWorkspace stale = await manager.CreateAsync(Guid.NewGuid(), default);
            Directory.CreateSymbolicLink(Path.Combine(stale.DirectoryPath, "escape"), outside);
            await manager.CleanupStaleAsync(default);
            Assert.True(File.Exists(Path.Combine(outside, "sentinel")));
            Directory.CreateSymbolicLink(Path.Combine(root.Path, "root-link"), outside);
            var unsafeManager = new CrashWorkspace(new() { ScratchRoot = Path.Combine(root.Path, "root-link", "nested") });
            await Assert.ThrowsAsync<IOException>(() => unsafeManager.CreateAsync(Guid.NewGuid(), default));
        }
        finally
        {
            Directory.Delete(outside, true);
        }
    }

    private sealed class TestRoot : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "workspace-" + Guid.NewGuid().ToString("N"));
        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
