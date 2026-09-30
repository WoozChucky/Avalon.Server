using System.Text.Json;
using Avalon.Balance.Contract;
using Avalon.Balance.Core;
using Avalon.Balance.Service.Runs;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Avalon.Balance.Service.UnitTests;

public class RunQueueShould
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));
    private readonly BalanceServiceOptions _options = new();
    private readonly BalanceHost _host;
    private readonly RunQueue _queue;

    public RunQueueShould()
    {
        using WebApplication app = BalanceTestHost.Build();
        _host = app.Services.GetRequiredService<BalanceHost>();
        _queue = new RunQueue(Options.Create(_options), _time);
    }

    private static RunRequestDto Small(int runs = 10) =>
        new(null, null, new RunFilterDto(["Warrior"], null, null, ["normal-3"]), runs, null);

    private RunWorker Worker() =>
        new(_queue, _host, _time, Options.Create(_options), NullLogger<RunWorker>.Instance);

    private string Enqueue(RunRequestDto dto)
    {
        Assert.Equal(EnqueueOutcome.Accepted, _queue.TryEnqueue(dto, out string id, out _));
        return id;
    }

    private async Task<RunRecord> WaitFor(string id, Func<RunRecord, bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (true)
        {
            RunRecord? record = _queue.Get(id);
            if (record is not null && condition(record))
                return record;
            await Task.Delay(5, timeout.Token);
        }
    }

    [Fact]
    public async Task Take_a_run_from_queued_through_running_to_done_with_its_result()
    {
        using RunWorker worker = Worker();
        string id = Enqueue(Small());
        Assert.Equal(RunState.Queued, _queue.Get(id)!.Status);

        await worker.StartAsync(CancellationToken.None);
        RunRecord record = await WaitFor(id, r => r.IsFinished);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(RunState.Done, record.Status);
        Assert.NotNull(record.Result);
        Assert.NotEmpty(record.Result.Rows);
        Assert.All(record.Result.Rows, r => Assert.Equal("Warrior", r.Class));
        Assert.Equal(record.Progress.RowsTotal, record.Progress.RowsDone);
        Assert.Equal(record.Result.Rows.Count, record.Progress.RowsDone);
        Assert.Equal(_time.GetUtcNow(), record.FinishedAt);
        Assert.Empty(record.Issues);
    }

    [Fact]
    public async Task Report_running_while_the_simulation_runs()
    {
        using RunWorker worker = Worker();
        string id = Enqueue(new RunRequestDto(null, null, null, 1000, null));

        await worker.StartAsync(CancellationToken.None);
        RunRecord record = await WaitFor(id, r => r.Status == RunState.Running);
        _queue.Cancel(id);
        await WaitFor(id, r => r.IsFinished);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(RunState.Cancelled, record.Status);
    }

    [Fact]
    public async Task Refuse_the_fifth_run_even_when_requests_race()
    {
        // No worker started: nothing drains, so every accepted run stays queued.
        var outcomes = new EnqueueOutcome[8];
        using var gate = new Barrier(outcomes.Length);
        Task[] tasks = Enumerable.Range(0, outcomes.Length).Select(i => Task.Run(() =>
        {
            gate.SignalAndWait();
            outcomes[i] = _queue.TryEnqueue(Small(), out _, out _);
        })).ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(4, outcomes.Count(o => o == EnqueueOutcome.Accepted));
        Assert.Equal(4, outcomes.Count(o => o == EnqueueOutcome.Full));
    }

    [Fact]
    public void Free_a_slot_when_a_queued_run_is_cancelled()
    {
        string[] ids = Enumerable.Range(0, 4).Select(_ => Enqueue(Small())).ToArray();
        Assert.Equal(EnqueueOutcome.Full, _queue.TryEnqueue(Small(), out _, out _));

        Assert.True(_queue.Cancel(ids[2]));

        Assert.Equal(EnqueueOutcome.Accepted, _queue.TryEnqueue(Small(), out _, out _));
    }

    [Fact]
    public async Task Never_run_a_queued_run_that_was_cancelled()
    {
        using RunWorker worker = Worker();
        string cancelled = Enqueue(Small());
        Assert.True(_queue.Cancel(cancelled));
        Assert.Equal(RunState.Cancelled, _queue.Get(cancelled)!.Status);

        await worker.StartAsync(CancellationToken.None);
        string next = Enqueue(Small());
        await WaitFor(next, r => r.IsFinished);
        await worker.StopAsync(CancellationToken.None);

        RunRecord record = _queue.Get(cancelled)!;
        Assert.Equal(RunState.Cancelled, record.Status);
        Assert.Null(record.Result);
        Assert.Equal((0, 0), record.Progress);
    }

    [Fact]
    public async Task End_a_cancelled_running_run_cancelled_with_no_rows()
    {
        using RunWorker worker = Worker();
        string id = Enqueue(new RunRequestDto(null, null, null, 1000, null));
        await worker.StartAsync(CancellationToken.None);
        await WaitFor(id, r => r.Status == RunState.Running);

        Assert.True(_queue.Cancel(id));
        RunRecord record = await WaitFor(id, r => r.IsFinished);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(RunState.Cancelled, record.Status);
        Assert.Null(record.Result);
    }

    [Fact]
    public void Answer_false_for_cancelling_an_unknown_run()
    {
        Assert.False(_queue.Cancel("nope"));
    }

    [Fact]
    public async Task Forget_a_result_an_hour_after_it_finished()
    {
        using RunWorker worker = Worker();
        string id = Enqueue(Small());
        await worker.StartAsync(CancellationToken.None);
        await WaitFor(id, r => r.IsFinished);
        await worker.StopAsync(CancellationToken.None);

        _time.Advance(TimeSpan.FromMinutes(59));
        _queue.Sweep(_time.GetUtcNow());
        Assert.NotNull(_queue.Get(id));

        _time.Advance(TimeSpan.FromMinutes(2));
        _queue.Sweep(_time.GetUtcNow());
        Assert.Null(_queue.Get(id));
    }

    [Fact]
    public void Never_sweep_a_run_that_is_still_queued()
    {
        string id = Enqueue(Small());

        _time.Advance(TimeSpan.FromHours(5));
        _queue.Sweep(_time.GetUtcNow());

        Assert.Equal(RunState.Queued, _queue.Get(id)!.Status);
    }

    [Fact]
    public async Task Sweep_on_its_own_every_minute()
    {
        using RunWorker worker = Worker();
        string id = Enqueue(Small());
        await worker.StartAsync(CancellationToken.None);
        await WaitFor(id, r => r.IsFinished);

        // Step the clock a minute at a time, as real time would, so each tick fires.
        for (int i = 0; i < 65 && _queue.Get(id) is not null; i++)
        {
            _time.Advance(TimeSpan.FromMinutes(1));
            await Task.Delay(10);
        }

        await worker.StopAsync(CancellationToken.None);
        Assert.Null(_queue.Get(id));
    }

    [Fact]
    public void Refuse_too_many_runs_per_row()
    {
        EnqueueOutcome outcome = _queue.TryEnqueue(Small(1001), out string id, out IReadOnlyList<Issue> issues);

        Assert.Equal(EnqueueOutcome.Invalid, outcome);
        Assert.Equal("", id);
        Assert.Contains(issues, i => i.Path == "runsPerRow");
    }

    [Fact]
    public void Refuse_too_many_overrides()
    {
        Dictionary<string, JsonElement> overrides = Enumerable.Range(0, 501).ToDictionary(
            i => $"CreatureBaseStats.{i}.Health", _ => JsonSerializer.SerializeToElement(1));

        EnqueueOutcome outcome = _queue.TryEnqueue(new RunRequestDto(overrides, null, null, 10, null), out _, out IReadOnlyList<Issue> issues);

        Assert.Equal(EnqueueOutcome.Invalid, outcome);
        Assert.Contains(issues, i => i.Path == "overrides");
    }

    [Fact]
    public void Refuse_a_bad_filter_and_a_bad_config_with_their_paths()
    {
        var dto = new RunRequestDto(null, new BalanceConfigDto("{", "{}", "{}"), new RunFilterDto(["Bard"], null, null, null), 10, null);

        EnqueueOutcome outcome = _queue.TryEnqueue(dto, out _, out IReadOnlyList<Issue> issues);

        Assert.Equal(EnqueueOutcome.Invalid, outcome);
        Assert.Contains(issues, i => i.Path == "filter.classes");
        Assert.Contains(issues, i => i.Path == "config.scenarios");
    }

    [Fact]
    public async Task Report_a_refusal_found_by_the_simulation_as_invalid_with_its_issues()
    {
        using RunWorker worker = Worker();
        var overrides = new Dictionary<string, JsonElement> { ["Nope.1.Health"] = JsonSerializer.SerializeToElement(1) };
        string id = Enqueue(new RunRequestDto(overrides, null, null, 10, null));

        await worker.StartAsync(CancellationToken.None);
        RunRecord record = await WaitFor(id, r => r.IsFinished);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(RunState.Invalid, record.Status);
        Assert.NotEmpty(record.Issues);
        Assert.Null(record.Result);
    }

    [Fact]
    public void Give_each_run_a_random_32_character_hex_id()
    {
        string a = Enqueue(Small());
        string b = Enqueue(Small());

        Assert.NotEqual(a, b);
        Assert.Matches("^[0-9a-f]{32}$", a);
    }
}
