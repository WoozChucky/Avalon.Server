namespace Avalon.Crashes.Worker.Processing;

/// <summary>
/// Runs a pinned native tool. Every failed invocation disposes its supplied workspace; callers must not reuse it.
/// Optional conversions must use separate leases from the same CrashWorkspace manager/quota as the main report,
/// so failure cannot delete the report dump or previously resolved symbols. Successful leases remain caller-owned.
/// </summary>
public interface ICrashToolRunner
{
    Task<ToolRunResult> RunAsync(ToolInvocation invocation, CancellationToken ct);
}

public enum CrashToolKind { DumpSymbols, Stackwalk }
public enum ToolFailureKind { None, InvalidInvocation, ToolIdentity, Start, NonzeroExit, WallTime, CpuTime, OutputLimit, ScratchLimit, Cancelled, Io }

/// <summary>
/// The workspace is owned for the full invocation and is disposed on any tool failure. Supply an isolated conversion
/// lease for optional work, sharing the main report's CrashWorkspace manager. Do not pass the main report lease to
/// a best-effort converter. Arguments and all inputs must remain within the supplied lease.
/// </summary>
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
