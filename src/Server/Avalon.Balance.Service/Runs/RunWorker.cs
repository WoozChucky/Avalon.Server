using Avalon.Balance.Contract;
using Avalon.Balance.Core;
using Avalon.Balance.Service.Mapping;
using Microsoft.Extensions.Options;

namespace Avalon.Balance.Service.Runs;

/// <summary>Runs the queued simulations one at a time, and sweeps expired results every minute.</summary>
public sealed class RunWorker : BackgroundService
{
    private static readonly TimeSpan s_sweepEvery = TimeSpan.FromMinutes(1);
    private static readonly IReadOnlyList<IssueDto> s_failedIssues = [new IssueDto("run", "the run failed")];

    private readonly RunQueue _queue;
    private readonly BalanceHost _host;
    private readonly TimeProvider _time;
    private readonly ILogger<RunWorker> _logger;
    private readonly bool _paused;
    private readonly RunSimulation _simulate;

    public RunWorker(RunQueue queue, BalanceHost host, TimeProvider time, IOptions<BalanceServiceOptions> options, ILogger<RunWorker> logger,
        RunSimulation? simulate = null)
    {
        _queue = queue;
        _host = host;
        _time = time;
        _logger = logger;
        _simulate = simulate ?? Simulation.Run;
        // The test seam: a paused worker never drains, so a test can fill the queue. Sweeping still runs.
        _paused = !options.Value.RunWorker;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(_paused ? Task.CompletedTask : DrainAsync(stoppingToken), SweepAsync(stoppingToken));

    private async Task SweepAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(s_sweepEvery, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                _queue.Sweep(_time.GetUtcNow());
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task DrainAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            RunRecord record;
            try
            {
                record = await _queue.TakeNextAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // A run is minutes of CPU: a thread of its own, not a pool thread the host needs.
            await Task.Factory.StartNew(() => Execute(record, stoppingToken), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default).ConfigureAwait(false);
        }
    }

    /// <summary>Runs one record to its end state. Never throws.</summary>
    private void Execute(RunRecord record, CancellationToken stoppingToken)
    {
        RunRequest request = record.Request!; // set until the run ends, and only this worker ends a running record
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                record.CancellationTokenSource.Token, stoppingToken);
            RunResult result = _simulate(_host.Seed, _host.Defaults, request,
                new RecordProgress(record), linked.Token);

            if (linked.IsCancellationRequested || result.Status == RunStatus.Cancelled)
                _queue.Complete(record, RunState.Cancelled, null, []);
            else if (result.Status == RunStatus.Invalid)
                _queue.Complete(record, RunState.Invalid, null, result.Issues.Select(WireMapping.ToDto).ToList());
            else
                _queue.Complete(record, RunState.Done, WireMapping.ToDto(result), []);
        }
        catch (Exception e)
        {
            // The run id and the exception only, never the request: overrides and config are caller data.
            _logger.LogError(e, "Run {RunId} failed", record.Id);
            _queue.Complete(record, RunState.Failed, null, s_failedIssues);
        }
    }

    private sealed class RecordProgress(RunRecord record) : IProgress<RunProgress>
    {
        public void Report(RunProgress value) => record.ReportProgress(value);
    }
}
