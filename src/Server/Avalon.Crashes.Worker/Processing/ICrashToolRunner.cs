namespace Avalon.Crashes.Worker.Processing;

public interface ICrashToolRunner
{
    Task<ToolRunResult> RunAsync(ToolInvocation invocation, CancellationToken ct);
}

public enum CrashToolKind { DumpSymbols, Stackwalk }
public enum ToolFailureKind { None, InvalidInvocation, ToolIdentity, Start, NonzeroExit, WallTime, CpuTime, OutputLimit, ScratchLimit, Cancelled, Io }

public sealed record ToolInvocation(
    CrashToolKind Kind,
    IReadOnlyList<string> Arguments,
    CrashAttemptWorkspace Workspace,
    TimeSpan WallTimeout,
    int CpuSeconds,
    long StdoutLimitBytes,
    long StderrLimitBytes);

public sealed record ToolRunResult(int? ExitCode, string? OutputPath, string? ErrorPath,
    ToolFailureKind Failure, long OutputBytes = 0, long ErrorBytes = 0);
