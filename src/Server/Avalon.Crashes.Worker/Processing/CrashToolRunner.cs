using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Avalon.Crashes.Worker.Processing;

/// <summary>Linux-only boundary for the two pinned native tools. Never runs an input executable.</summary>
public sealed class CrashToolRunner : ICrashToolRunner
{
    // A subreaper belongs to the process; serialize native invocations so group-specific reaping has one owner.
    private static readonly SemaphoreSlim s_nativeGate = new(1, 1);
    private readonly CrashToolOptions _options;
    private readonly bool _testHelper;
    private readonly TimeProvider _timeProvider;

    public CrashToolRunner(CrashToolOptions options, TimeProvider? timeProvider = null)
    {
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    // Used only by the real Linux tests through reflection. Fixed helper and finite modes; no runtime configuration switch.
    internal CrashToolRunner()
    {
        _options = new();
        _timeProvider = TimeProvider.System;
        _testHelper = true;
    }

    /// <summary>Startup/readiness gate; full binary digests pin the versions recorded in native/tools.lock.json.</summary>
    public async Task<ToolFailureKind> ValidateToolsAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/setsid") || !File.Exists("/usr/bin/prlimit"))
            return ToolFailureKind.ToolIdentity;
        try
        {
            foreach (CrashToolKind kind in new[] { CrashToolKind.DumpSymbols, CrashToolKind.Stackwalk })
            {
                string name = kind == CrashToolKind.DumpSymbols ? "dump_syms" : "minidump-stackwalk";
                if (!await VerifiedToolAsync(Path.Combine(_options.ToolDirectory, name), kind, ct))
                    return ToolFailureKind.ToolIdentity;
            }
            return ToolFailureKind.None;
        }
        catch (IOException)
        {
            return ToolFailureKind.ToolIdentity;
        }
    }

    public async Task<ToolRunResult> RunAsync(ToolInvocation invocation, CancellationToken ct)
    {
        ToolRunResult result;
        bool acquired = false;
        try
        {
            invocation = invocation with { Arguments = invocation.Arguments.ToArray() };
            await s_nativeGate.WaitAsync(ct);
            acquired = true;
            if (!OperatingSystem.IsLinux() || !Valid(invocation))
                result = new(null, null, null, ToolFailureKind.InvalidInvocation);
            else
                result = await RunCoreAsync(invocation, ct);
        }
        catch (OperationCanceledException)
        {
            result = new(null, null, null, ToolFailureKind.Cancelled);
        }
        catch (CrashScratchLimitException)
        {
            result = new(null, null, null, ToolFailureKind.ScratchLimit);
        }
        catch (Win32Exception)
        {
            result = new(null, null, null, ToolFailureKind.Start);
        }
        catch (IOException)
        {
            result = new(null, null, null, ToolFailureKind.Io);
        }
        catch (ArgumentException)
        {
            result = new(null, null, null, ToolFailureKind.InvalidInvocation);
        }
        finally
        {
            if (acquired)
                s_nativeGate.Release();
        }
        if (result.Failure != ToolFailureKind.None)
            await invocation.Workspace.DisposeAsync();
        return result;
    }

    private bool Valid(ToolInvocation invocation)
    {
        bool conversion = invocation.Kind == CrashToolKind.DumpSymbols;
        if (invocation.Kind is not (CrashToolKind.DumpSymbols or CrashToolKind.Stackwalk)
            || invocation.WallTimeout <= TimeSpan.Zero || invocation.WallTimeout > TimeSpan.FromSeconds(conversion ? 120 : 60)
            || invocation.CpuSeconds <= 0 || invocation.CpuSeconds > (conversion ? 90 : 45)
            || invocation.StdoutLimitBytes <= 0 || invocation.StdoutLimitBytes > (conversion ? 256L * 1024 * 1024 : 16L * 1024 * 1024)
            || invocation.StderrLimitBytes is <= 0 or > 64 * 1024)
        {
            return false;
        }
        invocation.Workspace.Verify();
        IReadOnlyList<string> args = invocation.Arguments;
        if (_testHelper)
            return args.Count == 1 && args[0] is "descendant" or "orphan" or "output" or "stdout" or "stderr" or "cpu" or "environment" or "nonzero";
        if (conversion)
            return args.Count == 3 && args[0] == "--inlines" && args[1] == "--check-cfi" && LocalPath(args[2], invocation.Workspace.DirectoryPath, directory: false);
        return args.Count == 3 && args[0] == "--json" && LocalPath(args[1], invocation.Workspace.DirectoryPath, directory: false)
            && LocalPath(args[2], invocation.Workspace.DirectoryPath, directory: true);
    }

    private static bool LocalPath(string path, string attempt, bool directory)
    {
        if (!Path.IsPathFullyQualified(path) || !path.StartsWith(attempt + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return false;
        string full = Path.GetFullPath(path);
        if (!full.StartsWith(attempt + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return false;
        for (string? current = full; current != attempt; current = Path.GetDirectoryName(current))
        {
            if (current is null || (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                return false;
        }
        return directory ? Directory.Exists(full) : File.Exists(full);
    }

    private async Task<ToolRunResult> RunCoreAsync(ToolInvocation invocation, CancellationToken ct)
    {
        string executable = _testHelper ? "/bin/bash" : Path.Combine(_options.ToolDirectory,
            invocation.Kind == CrashToolKind.DumpSymbols ? "dump_syms" : "minidump-stackwalk");
        if (!_testHelper && !await VerifiedToolAsync(executable, invocation.Kind, ct))
            return new(null, null, null, ToolFailureKind.ToolIdentity);
        if (Native.Prctl(36, 1, 0, 0, 0) != 0)
            return new(null, null, null, ToolFailureKind.Start);
        string outputName = Guid.NewGuid().ToString("N") + ".stdout";
        string errorName = Guid.NewGuid().ToString("N") + ".stderr";
        await using Stream output = await invocation.Workspace.CreateFileAsync(outputName, invocation.StdoutLimitBytes, ct);
        await using Stream error = await invocation.Workspace.CreateFileAsync(errorName, invocation.StderrLimitBytes, ct);
        var info = new ProcessStartInfo("/usr/bin/setsid")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = invocation.Workspace.DirectoryPath
        };
        info.Environment.Clear();
        info.Environment.Add("PATH", "/usr/bin:/bin");
        info.Environment.Add("HOME", invocation.Workspace.DirectoryPath);
        info.Environment.Add("TMPDIR", invocation.Workspace.DirectoryPath);
        info.Environment.Add("LANG", "C.UTF-8");
        info.ArgumentList.Add("/usr/bin/prlimit");
        info.ArgumentList.Add(string.Create(CultureInfo.InvariantCulture, $"--cpu={invocation.CpuSeconds}:{invocation.CpuSeconds + 1}"));
        info.ArgumentList.Add("--core=0:0");
        info.ArgumentList.Add("--");
        info.ArgumentList.Add(executable);
        if (_testHelper)
            info.ArgumentList.Add("/repo/tools/crashes/test-process.sh");
        foreach (string argument in invocation.Arguments)
            info.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = info };
        if (!process.Start())
            return new(null, null, null, ToolFailureKind.Start);
        process.StandardInput.Close();
        var overflow = new TaskCompletionSource<ToolFailureKind>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<long> stdout = DrainAsync(process.StandardOutput.BaseStream, output, invocation.StdoutLimitBytes, overflow);
        Task<long> stderr = DrainAsync(process.StandardError.BaseStream, error, invocation.StderrLimitBytes, overflow);
        Task exit = process.WaitForExitAsync(CancellationToken.None);
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var deadline = Task.Delay(invocation.WallTimeout, _timeProvider, timer.Token);
        ToolFailureKind failure;
        long outputBytes;
        long errorBytes;
        try
        {
            Task completed = await Task.WhenAny(exit, overflow.Task, deadline);
            failure = completed == deadline ? (ct.IsCancellationRequested ? ToolFailureKind.Cancelled : ToolFailureKind.WallTime)
                : completed == overflow.Task ? await overflow.Task : ToolFailureKind.None;
        }
        finally
        {
            // Always kill the session, even if its original leader exited successfully. Pipe-holding orphans cannot outlive it.
            int killed = Native.Kill(-process.Id, 9);
            if (killed != 0 && !process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* It exited between inspection and kill. */ }
                catch (Win32Exception) when (process.HasExited) { /* The process exited before the fallback kill. */ }
            }
            await exit;
            await ReapGroupAsync(process.Id);
            timer.Cancel();
            outputBytes = await stdout;
            errorBytes = await stderr;
        }
        if (overflow.Task.IsCompletedSuccessfully)
            failure = await overflow.Task;
        if (failure == ToolFailureKind.None && process.ExitCode != 0)
            failure = process.ExitCode == 128 + 24 ? ToolFailureKind.CpuTime : ToolFailureKind.NonzeroExit;
        return new(process.ExitCode,
            failure == ToolFailureKind.None ? Path.Combine(invocation.Workspace.DirectoryPath, outputName) : null,
            failure == ToolFailureKind.None ? Path.Combine(invocation.Workspace.DirectoryPath, errorName) : null,
            failure, outputBytes, errorBytes);
    }

    private static async Task<long> DrainAsync(Stream source, Stream destination, long cap, TaskCompletionSource<ToolFailureKind> failure)
    {
        byte[] buffer = new byte[8192];
        long written = 0;
        try
        {
            while (true)
            {
                int count = await source.ReadAsync(buffer, CancellationToken.None);
                if (count == 0)
                    return written;
                int accepted = (int)Math.Min(count, cap - written);
                if (accepted > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, accepted), CancellationToken.None);
                    written += accepted;
                }
                if (accepted < count)
                {
                    failure.TrySetResult(ToolFailureKind.OutputLimit);
                    return written;
                }
            }
        }
        catch (IOException)
        {
            failure.TrySetResult(ToolFailureKind.Io);
            return written;
        }
    }

    private static async Task<bool> VerifiedToolAsync(string executable, CrashToolKind kind, CancellationToken ct)
    {
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable) || (File.GetAttributes(executable) & FileAttributes.ReparsePoint) != 0)
            return false;
        await using FileStream file = File.OpenRead(executable);
        byte[] digest = await SHA256.HashDataAsync(file, ct);
        string expected = kind == CrashToolKind.DumpSymbols
            ? "7C8DE6B07BE9E3260A947FE428F3B19EF200D8C939C3C9D9E1462F028281DE74"
            : "BCCFA6806F8BC5EC96BFE6097C07FC715F8C9EFD28382632D952408B0E3CE358";
        // Full binary digest pins identity AND version to Task 1's lock, without executing an unverified --version.
        return string.Equals(Convert.ToHexString(digest), expected, StringComparison.Ordinal);
    }

    private static async Task ReapGroupAsync(int group)
    {
        while (true)
        {
            int reaped = Native.WaitPid(-group, out _, 1);
            if (reaped > 0)
                continue;
            int error = Marshal.GetLastPInvokeError();
            if (reaped == -1 && error == 10) // ECHILD: all adopted members of this process group have been reaped.
                return;
            if (reaped == -1 && error != 4) // EINTR may be retried; other failures must not pretend cleanup succeeded.
                throw new IOException("Could not reap native process group.");
            await Task.Delay(10, CancellationToken.None);
        }
    }

    private static class Native
    {
        [DllImport("libc", EntryPoint = "prctl", SetLastError = true)]
        internal static extern int Prctl(int option, ulong arg2, ulong arg3, ulong arg4, ulong arg5);
        [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
        internal static extern int Kill(int pid, int signal);
        [DllImport("libc", EntryPoint = "waitpid", SetLastError = true)]
        internal static extern int WaitPid(int pid, out int status, int options);
    }
}
