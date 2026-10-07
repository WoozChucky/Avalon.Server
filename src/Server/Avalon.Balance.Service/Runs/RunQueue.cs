using System.Security.Cryptography;
using System.Text.Json;
using Avalon.Balance.Contract;
using Avalon.Balance.Core;
using Avalon.Balance.Service.Mapping;
using Microsoft.Extensions.Options;

namespace Avalon.Balance.Service.Runs;


/// <summary>
/// Holds every run the service knows about. One lock covers admission (count the queued and running records, add the
/// new one), every state change and the sweep, so the limit holds however the requests race. The simulation itself
/// runs outside the lock, in <see cref="RunWorker" />.
/// </summary>
public sealed class RunQueue
{
    private readonly Lock _gate = new();
    private long _finishCounter;
    private readonly Dictionary<string, RunRecord> _records = new(StringComparer.Ordinal);
    private readonly Queue<RunRecord> _waiting = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly BalanceServiceOptions _options;
    private readonly TimeProvider _time;

    public RunQueue(IOptions<BalanceServiceOptions> options, TimeProvider time)
    {
        _options = options.Value;
        _time = time;
    }

    /// <summary>Maps the wire request (filter, config), then admits it like the <see cref="RunRequest" /> overload.</summary>
    public EnqueueOutcome TryEnqueue(RunRequestDto dto, out string runId, out IReadOnlyList<Issue> issues)
    {
        runId = "";
        var found = new List<Issue>();

        (RunFilter? filter, IReadOnlyList<IssueDto> filterIssues) = WireMapping.ToFilter(dto.Filter);
        found.AddRange(filterIssues.Select(i => new Issue(i.Path, i.Message)));

        BalanceConfig? config = null;
        if (dto.Config is { } configDto)
        {
            (config, IReadOnlyList<IssueDto> configIssues) = WireMapping.ToConfig(configDto);
            found.AddRange(configIssues.Select(i => new Issue(i.Path, i.Message)));
        }

        JsonElement? overrides = dto.Overrides is null ? null : JsonSerializer.SerializeToElement(dto.Overrides);

        if (found.Count > 0)
        {
            // The cheap caps too, so one response lists everything wrong.
            found.AddRange(CapIssues(overrides, dto.RunsPerRow));
            issues = found;
            return EnqueueOutcome.Invalid;
        }

        return TryEnqueue(new RunRequest(overrides, config, filter ?? RunFilter.None, dto.RunsPerRow, dto.Seed), out runId, out issues);
    }

    public EnqueueOutcome TryEnqueue(RunRequest request, out string runId, out IReadOnlyList<Issue> issues)
    {
        runId = "";
        List<Issue> caps = CapIssues(request.Overrides, request.RunsPerRow);
        if (caps.Count > 0)
        {
            issues = caps;
            return EnqueueOutcome.Invalid;
        }

        issues = [];
        lock (_gate)
        {
            int active = 0;
            foreach (RunRecord existing in _records.Values)
            {
                if (existing.Status is RunState.Queued or RunState.Running)
                    active++;
            }

            // The one running plus MaxQueued waiting.
            if (active >= _options.MaxQueued + 1)
                return EnqueueOutcome.Full;

            string id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var record = new RunRecord(id, request, new CancellationTokenSource());
            _records.Add(id, record);
            _waiting.Enqueue(record);
            _signal.Release();
            runId = id;
            return EnqueueOutcome.Accepted;
        }
    }

    public RunRecord? Get(string runId)
    {
        lock (_gate)
            return _records.GetValueOrDefault(runId);
    }

    /// <summary>
    /// A queued run becomes cancelled at once and never runs. A running one is told to stop and ends cancelled when the
    /// simulation returns, so the next run never overlaps it. A finished one is left as it is. False only for an unknown id.
    /// </summary>
    public bool Cancel(string runId)
    {
        lock (_gate)
        {
            if (!_records.TryGetValue(runId, out RunRecord? record))
                return false;

            switch (record.Status)
            {
                case RunState.Queued:
                    record.CancellationTokenSource.Cancel();
                    Finish(record, RunState.Cancelled, null, []);
                    break;
                case RunState.Running:
                    record.CancellationTokenSource.Cancel();
                    break;
            }

            return true;
        }
    }

    /// <summary>Forgets finished runs older than the TTL. Queued and running records are never removed.</summary>
    public void Sweep(DateTimeOffset now)
    {
        lock (_gate)
        {
            List<string>? expired = null;
            foreach ((string id, RunRecord record) in _records)
            {
                if (record.IsFinished && record.FinishedAt is { } at && now - at >= _options.ResultTtl)
                    (expired ??= []).Add(id);
            }

            if (expired is null)
                return;

            foreach (string id in expired)
            {
                _records.Remove(id, out RunRecord? record);
                record!.CancellationTokenSource.Dispose();
            }
        }
    }

    /// <summary>Waits for the oldest queued run, marks it running and returns it. Runs cancelled while waiting are skipped.</summary>
    public async Task<RunRecord> TakeNextAsync(CancellationToken ct)
    {
        while (true)
        {
            await _signal.WaitAsync(ct).ConfigureAwait(false);
            lock (_gate)
            {
                if (_waiting.TryDequeue(out RunRecord? record) && record.Status == RunState.Queued)
                {
                    record.SetStatus(RunState.Running);
                    return record;
                }
            }
        }
    }

    /// <summary>Ends a running record. Under the lock, so it cannot interleave with a sweep or an admission count.</summary>
    public void Complete(RunRecord record, RunState status, RunResultDto? result, IReadOnlyList<IssueDto> issues)
    {
        lock (_gate)
            Finish(record, status, result, issues);
    }

    /// <summary>Ends a record and keeps at most MaxRetainedFinished finished ones, oldest out first. Caller holds the lock.</summary>
    private void Finish(RunRecord record, RunState status, RunResultDto? result, IReadOnlyList<IssueDto> issues)
    {
        record.Finish(status, result, issues, _time.GetUtcNow(), ++_finishCounter);

        int finished = 0;
        foreach (RunRecord r in _records.Values)
        {
            if (r.IsFinished)
                finished++;
        }

        while (finished-- > _options.MaxRetainedFinished)
        {
            RunRecord? oldest = null;
            foreach (RunRecord r in _records.Values)
            {
                if (r.IsFinished && (oldest is null || r.FinishSequence < oldest.FinishSequence))
                    oldest = r;
            }

            if (oldest is null)
                break;

            _records.Remove(oldest.Id);
            oldest.CancellationTokenSource.Dispose();
        }
    }

    private List<Issue> CapIssues(JsonElement? overrides, int? runsPerRow)
    {
        var issues = new List<Issue>();
        if (runsPerRow is { } runs && runs > _options.MaxRunsPerRow)
            issues.Add(new Issue("runsPerRow", $"runsPerRow must be {_options.MaxRunsPerRow} or fewer, not {runs}"));

        if (overrides is { ValueKind: JsonValueKind.Object } given)
        {
            int count = 0;
            foreach (JsonProperty _ in given.EnumerateObject())
                count++;
            if (count > _options.MaxOverrides)
                issues.Add(new Issue("overrides", $"overrides must have {_options.MaxOverrides} keys or fewer, not {count}"));
        }

        return issues;
    }
}
