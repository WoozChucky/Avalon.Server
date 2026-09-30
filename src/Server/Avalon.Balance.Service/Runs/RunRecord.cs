using Avalon.Balance.Contract;
using Avalon.Balance.Core;

namespace Avalon.Balance.Service.Runs;

/// <summary>
/// One run's state. The queue moves <see cref="Status" /> (under its lock) and publishes it last, after the result,
/// issues and finish time, so a reader that sees a finished status sees everything that goes with it. Progress is one
/// packed long, so a reader never sees rows done from one report and rows total from another.
/// </summary>
public sealed class RunRecord
{
    private int _status;
    private long _progress;
    private long _finishedTicks;
    private RunResultDto? _result;
    private IReadOnlyList<IssueDto> _issues = [];

    private RunRequest? _request;
    private long _finishSequence;

    internal RunRecord(string id, RunRequest request, CancellationTokenSource cts)
    {
        Id = id;
        _request = request;
        CancellationTokenSource = cts;
    }

    public string Id { get; }

    /// <summary>The request, until the run ends: a finished record keeps no copy of up to 1 MiB of caller data.</summary>
    public RunRequest? Request => Volatile.Read(ref _request);

    internal long FinishSequence => Interlocked.Read(ref _finishSequence);

    public CancellationTokenSource CancellationTokenSource { get; }

    public RunState Status => (RunState)Volatile.Read(ref _status);

    public bool IsFinished => Status >= RunState.Done;

    /// <summary>Rows done and rows total, read together.</summary>
    public (int RowsDone, int RowsTotal) Progress
    {
        get
        {
            long packed = Interlocked.Read(ref _progress);
            return ((int)(packed >> 32), (int)(packed & 0xFFFFFFFFL));
        }
    }

    /// <summary>The mapped result, kept so repeated polls do not map it again.</summary>
    public RunResultDto? Result => Volatile.Read(ref _result);

    public IReadOnlyList<IssueDto> Issues => Volatile.Read(ref _issues);

    public DateTimeOffset? FinishedAt
    {
        get
        {
            long ticks = Interlocked.Read(ref _finishedTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public void ReportProgress(RunProgress progress) =>
        Interlocked.Exchange(ref _progress, ((long)progress.RowsDone << 32) | (uint)progress.RowsTotal);

    internal void SetStatus(RunState status) => Volatile.Write(ref _status, (int)status);

    internal void Finish(RunState status, RunResultDto? result, IReadOnlyList<IssueDto> issues, DateTimeOffset now, long sequence)
    {
        Volatile.Write(ref _request, null);
        Interlocked.Exchange(ref _finishSequence, sequence);
        Volatile.Write(ref _result, result);
        Volatile.Write(ref _issues, issues);
        Interlocked.Exchange(ref _finishedTicks, now.UtcTicks);
        if (status == RunState.Done)
        {
            (_, int total) = Progress;
            Interlocked.Exchange(ref _progress, ((long)total << 32) | (uint)total);
        }

        SetStatus(status);
    }
}
