using System.Diagnostics;
using Avalon.Crashes.Worker.Processing;
using Xunit;

namespace Avalon.Crashes.IntegrationTests.Processing;

public sealed class CrashToolRunnerShould
{
    private static CrashToolRunner Runner() => (CrashToolRunner)Activator.CreateInstance(typeof(CrashToolRunner), nonPublic: true)!;

    private static async Task<CrashAttemptWorkspace> Workspace()
    {
        Assert.True(OperatingSystem.IsLinux(), "Process integration requires actual Linux.");
        return await new CrashWorkspace(new() { ScratchRoot = Path.Combine(Path.GetTempPath(), "runner-" + Guid.NewGuid().ToString("N")) })
            .CreateAsync(Guid.NewGuid(), default);
    }

    private static ToolInvocation Invocation(CrashAttemptWorkspace workspace, string mode) =>
        new(CrashToolKind.DumpSymbols, [mode], workspace, TimeSpan.FromSeconds(1), 1, 4096, 4096);

    [IntegrationFact]
    public async Task Should_kill_descendants_on_timeout()
    {
        await using CrashAttemptWorkspace workspace = await Workspace();
        Task<ToolRunResult> run = Runner().RunAsync(Invocation(workspace, "descendant"), default);
        int child = await ChildPid(workspace);
        ToolRunResult result = await run.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal(ToolFailureKind.WallTime, result.Failure);
        Assert.False(Directory.Exists($"/proc/{child}"), "Descendant must be killed and reaped.");
        Assert.False(Directory.Exists(workspace.DirectoryPath));
    }

    [IntegrationFact]
    public async Task Should_reap_descendants_even_when_parent_exits_first()
    {
        await using CrashAttemptWorkspace workspace = await Workspace();
        Task<ToolRunResult> run = Runner().RunAsync(Invocation(workspace, "orphan"), default);
        int child = await ChildPid(workspace);
        _ = await run.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.False(Directory.Exists($"/proc/{child}"), "A leader exiting early must not leave live or zombie children.");
        Assert.True(Directory.Exists(workspace.DirectoryPath));
    }

    [IntegrationFact]
    public async Task Should_enforce_each_stream_cap_independently()
    {
        foreach (string mode in new[] { "stdout", "stderr" })
        {
            await using CrashAttemptWorkspace workspace = await Workspace();
            ToolRunResult result = await Runner().RunAsync(Invocation(workspace, mode), default).WaitAsync(TimeSpan.FromSeconds(8));
            Assert.Equal(ToolFailureKind.OutputLimit, result.Failure);
            Assert.Equal(4096, mode == "stdout" ? result.OutputBytes : result.ErrorBytes);
            Assert.False(Directory.Exists(workspace.DirectoryPath));
        }
    }

    [IntegrationFact]
    public async Task Should_reject_command_flags_urls_and_excessive_budgets()
    {
        foreach (string[] args in new[] { new[] { "--json", "https://example.invalid/dump", "/tmp/symbols" }, new[] { "--help" }, new[] { "; exit 0" } })
        {
            await using CrashAttemptWorkspace workspace = await Workspace();
            ToolRunResult result = await new CrashToolRunner(new()).RunAsync(Invocation(workspace, "ignored") with { Arguments = args }, default);
            Assert.Equal(ToolFailureKind.InvalidInvocation, result.Failure);
            Assert.False(Directory.Exists(workspace.DirectoryPath));
        }
        await using CrashAttemptWorkspace oversized = await Workspace();
        ToolRunResult excessive = await Runner().RunAsync(Invocation(oversized, "environment") with { WallTimeout = TimeSpan.FromSeconds(121) }, default);
        Assert.Equal(ToolFailureKind.InvalidInvocation, excessive.Failure);
    }

    [IntegrationFact]
    public async Task Should_reject_replaced_native_tool_without_executing_it()
    {
        await using CrashAttemptWorkspace workspace = await Workspace();
        string input = await workspace.WriteAsync("input.dmp", new byte[8], default);
        string executable = await workspace.WriteAsync("dump_syms", "untrusted tool"u8.ToArray(), default);
        ToolRunResult result = await new CrashToolRunner(new() { ToolDirectory = Path.GetDirectoryName(executable)! })
            .RunAsync(Invocation(workspace, "ignored") with { Arguments = ["--inlines", "--check-cfi", input] }, default);
        Assert.Equal(ToolFailureKind.ToolIdentity, result.Failure);
        Assert.False(Directory.Exists(workspace.DirectoryPath));
    }

    [IntegrationFact]
    public async Task Should_use_locked_production_tools_with_local_arguments()
    {
        Assert.Equal(ToolFailureKind.None, await new CrashToolRunner(new()).ValidateToolsAsync(default));
        foreach (CrashToolKind kind in new[] { CrashToolKind.DumpSymbols, CrashToolKind.Stackwalk })
        {
            await using CrashAttemptWorkspace workspace = await Workspace();
            string input = await workspace.WriteAsync("invalid-input", new byte[8], default);
            string symbols = Path.Combine(workspace.DirectoryPath, "symbols");
            Directory.CreateDirectory(symbols);
            string[] arguments = kind == CrashToolKind.DumpSymbols ? ["--inlines", "--check-cfi", input] : ["--json", input, symbols];
            ToolRunResult result = await new CrashToolRunner(new()).RunAsync(Invocation(workspace, "ignored") with { Kind = kind, Arguments = arguments }, default);
            Assert.Equal(ToolFailureKind.NonzeroExit, result.Failure);
            Assert.NotEqual(0, result.ExitCode);
            Assert.False(Directory.Exists(workspace.DirectoryPath));
        }
    }

    [IntegrationFact]
    public async Task Should_stop_at_stdout_and_stderr_caps()
    {
        await using CrashAttemptWorkspace workspace = await Workspace();
        ToolRunResult result = await Runner().RunAsync(Invocation(workspace, "output"), default).WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal(ToolFailureKind.OutputLimit, result.Failure);
        Assert.InRange(result.OutputBytes, 0, 4096);
        Assert.InRange(result.ErrorBytes, 0, 4096);
        Assert.False(Directory.Exists(workspace.DirectoryPath));
    }

    [IntegrationFact]
    public async Task Should_enforce_cpu_time_even_without_wall_timeout()
    {
        await using CrashAttemptWorkspace workspace = await Workspace();
        var elapsed = Stopwatch.StartNew();
        ToolRunResult result = await Runner().RunAsync(Invocation(workspace, "cpu") with { WallTimeout = TimeSpan.FromSeconds(30) }, default)
            .WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal(ToolFailureKind.CpuTime, result.Failure);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(8));
    }

    [IntegrationFact]
    public async Task Should_not_inherit_secret_environment()
    {
        Environment.SetEnvironmentVariable("AVALON_TEST_SECRET", "test-marker-never-echo");
        try
        {
            await using CrashAttemptWorkspace workspace = await Workspace();
            ToolRunResult result = await Runner().RunAsync(Invocation(workspace, "environment"), default);
            Assert.Equal(ToolFailureKind.None, result.Failure);
            Assert.Equal("false", (await File.ReadAllTextAsync(result.OutputPath!)).Trim());
        }
        finally
        {
            Environment.SetEnvironmentVariable("AVALON_TEST_SECRET", null);
        }
    }

    [IntegrationFact]
    public async Task Should_clean_scratch_after_cancellation()
    {
        await using CrashAttemptWorkspace workspace = await Workspace();
        using var cancellation = new CancellationTokenSource();
        Task<ToolRunResult> run = Runner().RunAsync(Invocation(workspace, "descendant") with { WallTimeout = TimeSpan.FromSeconds(30) }, cancellation.Token);
        int child = await ChildPid(workspace);
        cancellation.Cancel();
        ToolRunResult result = await run.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal(ToolFailureKind.Cancelled, result.Failure);
        Assert.False(Directory.Exists($"/proc/{child}"));
        Assert.False(Directory.Exists(workspace.DirectoryPath));
    }

    [IntegrationFact]
    public async Task Should_classify_nonzero_exit_without_raw_error()
    {
        await using CrashAttemptWorkspace workspace = await Workspace();
        ToolRunResult result = await Runner().RunAsync(Invocation(workspace, "nonzero"), default);
        Assert.Equal(23, result.ExitCode);
        Assert.Equal(ToolFailureKind.NonzeroExit, result.Failure);
        Assert.Null(result.OutputPath);
        Assert.False(Directory.Exists(workspace.DirectoryPath));
    }

    private static async Task<int> ChildPid(CrashAttemptWorkspace workspace)
    {
        string pidFile = Path.Combine(workspace.DirectoryPath, "child.pid");
        for (int i = 0; i < 200; i++)
        {
            if (File.Exists(pidFile) && int.TryParse(await File.ReadAllTextAsync(pidFile), out int pid))
                return pid;
            await Task.Delay(10);
        }
        throw new InvalidOperationException("Test helper did not create a child.");
    }
}
