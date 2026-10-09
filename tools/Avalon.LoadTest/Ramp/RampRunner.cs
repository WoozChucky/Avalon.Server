using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalon.LoadTest.Api;
using Avalon.LoadTest.Bots;
using Avalon.LoadTest.Runs;

namespace Avalon.LoadTest.Ramp;

/// <summary>The options of <c>ramp</c>, resolved against the run.</summary>
/// <param name="Mix">The behaviour shares, as <c>--mix</c> takes them (<see cref="Bots.Mix.Parse"/>).</param>
/// <param name="Start">The first step's bot count.</param>
/// <param name="Step">Bots added each step.</param>
/// <param name="Hold">How long each step holds: a 30 s settle, then the judged window (<see cref="RampRunner.JudgedWindow"/>).</param>
/// <param name="Max">The most bots, at most the run's size.</param>
/// <param name="Dial">The host to dial instead of the join reply's, or null.</param>
/// <param name="Prometheus">The Prometheus HTTP API's origin.</param>
/// <param name="Pod">The world server's pod, for its memory limit.</param>
/// <param name="SignInConcurrency">Sign-ins at once, the runner's and the refresher's together.</param>
public sealed record RampOptions(
    string Mix, int Start, int Step, TimeSpan Hold, int Max, IReadOnlyList<Limit> Limits, string? Dial, Uri Prometheus,
    string Pod, int SignInConcurrency);

/// <summary>One held step: what was there, what was measured, and the decision on it.</summary>
/// <param name="Index">The step's number from 1; a re-hold is a step of its own at the same count.</param>
/// <param name="Bots">
/// The live bots: those sent into the world that have not given up (<see cref="BotState.Stopped"/>, their context
/// lost), in the world or on their way back into it. The decider and the capacity count these.
/// </param>
/// <param name="ByBehaviour">The live bots by behaviour.</param>
/// <param name="GeneratorCpu">The bot PC's CPU use over the judged window, a fraction of its cores.</param>
/// <param name="GeneratorLagP95Ms">The input driver's lateness p95 over the judged window.</param>
public sealed record StepRecord(
    int Index, int Bots, IReadOnlyDictionary<BehaviourKind, int> ByBehaviour, ServerValues Server, StepClientValues Client,
    double GeneratorCpu, double GeneratorLagP95Ms, Decision Decision)
{
    /// <summary>Bots in the world at the hold's end (<see cref="BotState.InWorld"/>), a cross-check of <see cref="Bots"/>.</summary>
    public int InWorld { get; init; }

    /// <summary>
    /// The world's players online at the hold's end less the count before the ramp, the server's own cross-check of
    /// <see cref="Bots"/>; null when Prometheus could not say.
    /// </summary>
    public int? PlayersOnlineAdded { get; init; }
}

/// <summary>How a ramp ended.</summary>
/// <param name="Capacity">As <see cref="Decision.Capacity"/>; for <see cref="RampOutcome.Stopped"/>, the last passing count (null with none).</param>
/// <param name="FailedFirst">The confirmed breaches that stopped the ramp; empty for any other end.</param>
/// <param name="SignInRate">Wall time per bot signed in by the runner: the sign-in throughput identity gave.</param>
public sealed record RampResult(
    RampOutcome Outcome, int? Capacity, IReadOnlyList<Breach> FailedFirst, IReadOnlyList<StepRecord> Steps,
    string? ServerVersion, TimeSpan SignInRate, DateTimeOffset Started, DateTimeOffset Ended)
{
    /// <summary>Why a <see cref="RampOutcome.Stopped"/> ramp stopped (Ctrl+C, an error, sign-ins failing); null otherwise.</summary>
    public string? StopReason { get; init; }

    /// <summary>Bots the runner signed in.</summary>
    public int SignIns { get; init; }

    /// <summary>Sign-in failures over the ramp, the context refresher's included.</summary>
    public int SignInFailures { get; init; }

    /// <summary>
    /// Whether, after the stop, the world's players online came back to the count before the ramp; null while the stop
    /// sequence is still running (the report written as soon as the outcome is known).
    /// </summary>
    public bool? WorldDrained { get; init; }

    /// <summary>
    /// Sign-outs skipped because the API was taken as down (<see cref="SignOutBreaker"/>): those contexts expire within
    /// 5 minutes. 0 while the stop sequence is still running.
    /// </summary>
    public int SignOutsSkipped { get; init; }

    /// <summary>
    /// Leaves skipped because the world was taken as hung (the leave <see cref="Breaker"/>): their sockets were closed
    /// at once. 0 while the stop sequence is still running.
    /// </summary>
    public int LeavesSkipped { get; init; }
}

/// <summary>
/// The capacity run. Bots are added in steps (sign in, then enter, each with its behaviour from the mix), every step is
/// held for <see cref="RampOptions.Hold"/>, and at its end the client values, the bot PC's and Prometheus's are judged by
/// the <see cref="RampDecider"/>: the next step, a re-hold at the same count, or the end. The next step's bots sign in
/// while the current one holds. Any end (the decider's, Ctrl+C, an error) runs the stop sequence: every bot leaves and
/// signs out, then the runner waits for the world's players online to come back to the count before the ramp.
/// </summary>
public sealed class RampRunner(RunFile run, RampOptions options)
{
    /// <summary>The first part of each hold, not judged: the step's entries and the server settle.</summary>
    public static TimeSpan Settle { get; } = TimeSpan.FromSeconds(30);

    /// <summary>The longest judged window.</summary>
    private static readonly TimeSpan s_maxWindow = TimeSpan.FromSeconds(60);

    /// <summary>The REST calls' own timeout.</summary>
    private static readonly TimeSpan s_apiTimeout = TimeSpan.FromSeconds(30);

    /// <summary>One bot's leave and sign-out at the end gets this long.</summary>
    private static readonly TimeSpan s_leaveTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// After a hold's end, Prometheus is read this much later (still for the window ending at the hold's end), so the
    /// world's last 10 s export of the window has landed.
    /// </summary>
    private static readonly TimeSpan s_exportLag = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan s_drainPoll = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan s_drainLimit = TimeSpan.FromSeconds(90);

    /// <summary>Bots entering, and leaving, at once.</summary>
    private const int Concurrency = 32;

    private readonly IReadOnlyList<(BehaviourKind Kind, int Weight)> _mix = Bots.Mix.Parse(options.Mix);
    private readonly Lock _botsLock = new();

    /// <summary>Every bot signed in, entered or not. Guarded by <see cref="_botsLock"/>.</summary>
    private readonly List<Bot> _bots = [];

    /// <summary>Bots signed in ahead of their step, in index order. Touched by the ramp loop and the sign-in batch it awaits.</summary>
    private readonly Queue<Bot> _ready = new();

    /// <summary>Bots sent into the world.</summary>
    private readonly List<Bot> _added = [];

    /// <summary>Each added bot's <see cref="BotLife"/> loop.</summary>
    private readonly List<Task> _lives = [];

    private IReadOnlyCollection<Bot>? _snapshot;

    /// <summary>The run's sign-out breaker, shared by every bot and the refresher.</summary>
    private readonly SignOutBreaker _signOuts = new();

    /// <summary>The run's leave breaker, shared by every bot.</summary>
    private readonly Breaker _leaves = new();
    private int _nextIndex;
    private int _signIns;
    private TimeSpan _signInTime;

    /// <summary>The judged window of a hold: its last 60 s, after the settle.</summary>
    public static TimeSpan JudgedWindow(TimeSpan hold) => hold - Settle < s_maxWindow ? hold - Settle : s_maxWindow;

    /// <summary>
    /// Runs the ramp and the stop sequence; the result is returned whatever ended it. Cancelling <paramref name="ct"/>
    /// (Ctrl+C) stops it as <see cref="RampOutcome.Stopped"/>; the stop sequence runs on its own timeouts.
    /// </summary>
    /// <exception cref="PrometheusException">Prometheus cannot be read before the first bot signs in.</exception>
    public Task<RampResult> RunAsync(CancellationToken ct) => RunAsync(outcomeKnown: null, ct);

    /// <summary>
    /// As <see cref="RunAsync(CancellationToken)"/>, calling <paramref name="outcomeKnown"/> with the result as soon as
    /// the outcome is known, before the stop sequence (minutes with many bots), its <see cref="RampResult.WorldDrained"/>
    /// still null: a report written there survives a second Ctrl+C. A failure of the callback is reported and the stop
    /// sequence goes on.
    /// </summary>
    public async Task<RampResult> RunAsync(Action<RampResult>? outcomeKnown, CancellationToken ct)
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        using var prometheus = new PrometheusClient(options.Prometheus, run.WorldId, options.Pod);
        string? version = await prometheus.ServerVersionAsync(ct);
        int playersBefore = await prometheus.PlayersOnlineAsync(ct);
        Console.WriteLine(Invariant(
            $"World {run.WorldId} ({version ?? "version unknown"}), {playersBefore} players online before the ramp."));

        using var api = new ApiClient(run.Api, s_apiTimeout);
        var metrics = new BotMetrics();
        using var signIns = new SemaphoreSlim(options.SignInConcurrency);
        var driver = new InputDriver(Snapshot);
        var refresher = new ContextRefresher(api, Snapshot, metrics, signIns, _signOuts);
        using var background = new CancellationTokenSource();
        using var refreshes = new CancellationTokenSource();
        using var signInsAgain = new CancellationTokenSource();
        using var lives = new CancellationTokenSource();
        using var presigning = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task driving = driver.RunAsync(background.Token);
        Task refreshing = refresher.RunAsync(refreshes.Token, signInsAgain.Token);

        var steps = new List<StepRecord>();
        var decider = new RampDecider(options.Limits, options.Max);
        RampOutcome outcome = RampOutcome.Stopped;
        int? capacity = null;
        IReadOnlyList<Breach> failedFirst = [];
        string? stopReason = null;
        int signInFailures = 0;
        Task? presign = null;
        try
        {
            int target = Math.Min(options.Start, options.Max);
            bool fill = true;
            while (true)
            {
                if (fill)
                {
                    if (presign is not null) await presign;
                    presign = null;
                    if (await FillAsync(target, api, metrics, signIns, lives.Token, ct) == 0)
                    {
                        // Short of --max with no bot to add: the ramp cannot go on, and no limit decided it.
                        (outcome, capacity, stopReason) = (RampOutcome.Stopped, LastPass(steps), _nextIndex >= run.Bots.Count
                            ? $"the run's {run.Bots.Count} accounts ran out before {target} live bots (sign-ins failed or bots gave up)"
                            : "no further bot could sign in");
                        break;
                    }
                }

                // The next step's bots sign in while this one holds (a re-hold's are signing in already, or ready).
                if (presign is null && target < options.Max)
                {
                    int nextNeed = Math.Min(target + options.Step, options.Max) - LiveCount() - _ready.Count;
                    if (nextNeed > 0) presign = SignInAsync(nextNeed, api, metrics, signIns, presigning.Token);
                }

                StepRecord step = await HoldAsync(steps.Count + 1, prometheus, playersBefore, metrics, driver, decider, ct);
                steps.Add(step);
                signInFailures += step.Client.SignInFailures;
                Console.WriteLine(StepLine(step));

                Decision decision = step.Decision;
                if (decision.Action == RampAction.Stop)
                {
                    outcome = decision.Outcome;
                    capacity = decision.Capacity;
                    failedFirst = decision.Outcome is RampOutcome.Capacity or RampOutcome.GeneratorSaturated ? decision.Breaches : [];
                    break;
                }

                fill = decision.Action == RampAction.NextStep;
                if (fill) target = Math.Min(target + options.Step, options.Max);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            (outcome, capacity, stopReason) = (RampOutcome.Stopped, LastPass(steps), "Ctrl+C");
        }
        catch (Exception error)
        {
            (outcome, capacity, stopReason) = (RampOutcome.Stopped, LastPass(steps), $"error: {error.Message}");
            Console.Error.WriteLine($"The ramp failed: {error}");
        }

        var partial = new RampResult(outcome, capacity, failedFirst, steps, version, SignInRate(), started, DateTimeOffset.UtcNow)
        {
            StopReason = stopReason,
            SignIns = _signIns,
            SignInFailures = signInFailures,
            WorldDrained = null,
        };
        try
        {
            outcomeKnown?.Invoke(partial);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Writing the report before the stop failed: {error.Message}");
        }

        Console.WriteLine(Invariant($"Stopping: {_added.Count} bots leave the world."));
        await presigning.CancelAsync();
        if (presign is not null) await presign.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        await lives.CancelAsync();
        await Task.WhenAll(_lives).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        // The re-sign-ins stop before the leaves, so no new context lands after a bot signed out; the refresh passes go
        // on through the leaves, so a bot waiting for a leave slot keeps its context and its world session's lease.
        await signInsAgain.CancelAsync();
        await refresher.SigningInAgain.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        await LeaveAllAsync();
        // The pass running now is seen through, and contexts whose refresh got no answer refreshed again, before the
        // refresher completes: a context the server rotated after its bot's sign-out is signed out there.
        await refreshes.CancelAsync();
        await background.CancelAsync();
        await Task.WhenAll(driving, refreshing).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        DisposeBots();
        if (_leaves.Skipped > 0)
        {
            Console.Error.WriteLine(Invariant($"World unresponsive: {_leaves.Skipped} leaves skipped; sockets closed."));
        }

        if (_signOuts.Skipped > 0)
        {
            Console.Error.WriteLine(Invariant(
                $"API down: {_signOuts.Skipped} sign-outs skipped; those contexts expire within 5 minutes."));
        }

        signInFailures += metrics.TakeWindow().SignInFailures;
        bool drained = await WaitForDrainAsync(prometheus, playersBefore);

        // The count and the rate from one moment: sign-ins still landing when the outcome was known count in both.
        return partial with
        {
            SignIns = _signIns,
            SignInRate = SignInRate(),
            Ended = DateTimeOffset.UtcNow,
            SignInFailures = signInFailures,
            WorldDrained = drained,
            SignOutsSkipped = _signOuts.Skipped,
            LeavesSkipped = _leaves.Skipped,
        };
    }

    /// <summary>
    /// The limits' values for a step: the server's, the bots' (admission = entry failures ÷ entry attempts, 0 with no
    /// attempt) and the bot PC's.
    /// </summary>
    public static IReadOnlyDictionary<LimitName, double?> Values(ServerValues server, StepClientValues client, double generatorCpu,
        double generatorLagP95Ms)
    {
        int failures = client.EntryFailures.Values.Sum();
        return new Dictionary<LimitName, double?>
        {
            [LimitName.TickP99] = server.TickP99Ms,
            [LimitName.Tps] = server.Tps,
            [LimitName.AckP95] = client.AckP95,
            [LimitName.Drops] = server.Drops,
            [LimitName.Admission] = client.EntryAttempts == 0 ? 0 : (double)failures / client.EntryAttempts,
            [LimitName.Memory] = server.WorkingSetFraction,
            [LimitName.Gen2] = server.Gen2PerMin,
            [LimitName.GcPause] = server.GcPauseFraction,
            [LimitName.SaveP95] = server.SaveP95Ms,
            [LimitName.GenCpu] = generatorCpu,
            [LimitName.GenLag] = generatorLagP95Ms,
        };
    }

    /// <summary>The limits a step could not be judged on: a missing or non-finite value (a missing drops value is no drops).</summary>
    public static IReadOnlyList<Limit> Unknowns(StepRecord step, IReadOnlyList<Limit> limits)
    {
        IReadOnlyDictionary<LimitName, double?> values = Values(step.Server, step.Client, step.GeneratorCpu, step.GeneratorLagP95Ms);
        return limits.Where(limit => values.TryGetValue(limit.Name, out double? value) && value is { } v
                ? !double.IsFinite(v)
                : limit.Name != LimitName.Drops)
            .ToList();
    }

    /// <summary>
    /// Brings the bots sent into the world up to <paramref name="target"/>: the ones signed in ahead first, more signed in
    /// now if they fall short, then all entered at once (32 at a time), each with its behaviour from the mix. A bot
    /// whose entry fails every attempt still gets its <see cref="BotLife"/>, which enters it again after a pause: its
    /// failures go on counting against admission rather than the bot quietly dropping out. A bot that gave up (its
    /// context lost) no longer counts towards the target: another account takes its place. Returns the bots entered.
    /// </summary>
    private async Task<int> FillAsync(int target, ApiClient api, BotMetrics metrics, SemaphoreSlim signIns,
        CancellationToken life, CancellationToken ct)
    {
        int need = target - LiveCount();
        if (need <= 0) return 0;

        if (_ready.Count < need) await SignInAsync(need - _ready.Count, api, metrics, signIns, ct);

        var entering = new List<Bot>();
        while (entering.Count < need && _ready.TryDequeue(out Bot? bot)) entering.Add(bot);
        if (entering.Count == 0) return 0;

        Console.Error.WriteLine(Invariant($"  entering {entering.Count} bots ({LiveCount() + entering.Count} live)"));
        await Parallel.ForEachAsync(entering, new ParallelOptions { MaxDegreeOfParallelism = Concurrency, CancellationToken = ct },
            async (bot, token) =>
            {
                bot.Behaviour = Bots.Mix.For(bot.Index, _mix);
                try
                {
                    await bot.EnterAsync(takeover: false, token);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    // Counted by the bot; its life enters it again.
                }

                lock (_botsLock)
                {
                    _added.Add(bot);
                    _lives.Add(BotLife.RunAsync(bot, metrics, life));
                }
            });
        return entering.Count;
    }

    /// <summary>
    /// Signs in <paramref name="count"/> more bots of the run, in index order, at most the semaphore's count at once;
    /// a bot whose sign-in fails (counted) is passed over for the next account. Stops early when a whole round failed or
    /// the run's accounts ran out.
    /// </summary>
    private async Task SignInAsync(int count, ApiClient api, BotMetrics metrics, SemaphoreSlim signIns, CancellationToken ct)
    {
        int signedIn = 0;
        while (signedIn < count && _nextIndex < run.Bots.Count)
        {
            int round = Math.Min(count - signedIn, run.Bots.Count - _nextIndex);
            var bots = new Bot[round];
            for (int i = 0; i < round; i++, _nextIndex++)
            {
                bots[i] = new Bot(_nextIndex, run.Bots[_nextIndex], run.BotPassword, api, run.WorldId, options.Dial,
                    metrics, _signOuts, _leaves);
            }

            long start = Stopwatch.GetTimestamp();
            bool[] ok = await Task.WhenAll(bots.Select(bot => SignInOneAsync(bot, signIns, ct)));
            int succeeded = ok.Count(x => x);
            _signInTime += Stopwatch.GetElapsedTime(start);
            _signIns += succeeded;
            signedIn += succeeded;

            for (int i = 0; i < round; i++)
            {
                if (ok[i]) _ready.Enqueue(bots[i]);
            }

            if (succeeded == 0) return;
        }
    }

    /// <summary>
    /// Signs one bot in; once signed in it is kept at once (refreshed, signed out and disposed at the end) even if the
    /// round it is part of is cancelled. A bot not kept is disposed here.
    /// </summary>
    private async Task<bool> SignInOneAsync(Bot bot, SemaphoreSlim signIns, CancellationToken ct)
    {
        bool kept = false;
        try
        {
            await signIns.WaitAsync(ct);
            try
            {
                await bot.SignInAsync(ct);
                lock (_botsLock)
                {
                    _bots.Add(bot);
                    _snapshot = null;
                }

                kept = true;
                return true;
            }
            catch (BotStepException)
            {
                // Counted by the bot.
                return false;
            }
            finally
            {
                signIns.Release();
            }
        }
        finally
        {
            if (!kept) bot.Dispose();
        }
    }

    /// <summary>
    /// Holds a step: the settle, then the judged window, at whose end the bots' window, the bot PC's CPU and lateness and
    /// Prometheus are read and the decider asked. The settle's entry and sign-in counts count towards the step; its
    /// acks do not.
    /// </summary>
    private async Task<StepRecord> HoldAsync(int index, PrometheusClient prometheus, int playersBefore, BotMetrics metrics,
        InputDriver driver, RampDecider decider, CancellationToken ct)
    {
        TimeSpan window = JudgedWindow(options.Hold);
        await Task.Delay(options.Hold - window, ct);
        StepClientValues settle = metrics.TakeWindow();
        driver.ResetWindow();
        using var process = Process.GetCurrentProcess();
        TimeSpan cpuBefore = process.TotalProcessorTime;
        long windowStart = Stopwatch.GetTimestamp();

        await Task.Delay(window, ct);

        DateTimeOffset end = DateTimeOffset.UtcNow;
        StepClientValues judged = metrics.TakeWindow();
        double lag = driver.LatenessP95Ms();
        process.Refresh();
        double cpu = (process.TotalProcessorTime - cpuBefore) / (Stopwatch.GetElapsedTime(windowStart) * Environment.ProcessorCount);
        Bot[] live = [.. _added.Where(bot => bot.State != BotState.Stopped)];
        int inWorld = live.Count(bot => bot.State == BotState.InWorld);

        await Task.Delay(s_exportLag, ct);
        ServerValues server = await prometheus.SampleAsync(end, window, ct);
        int? playersAdded = null;
        try
        {
            playersAdded = await prometheus.PlayersOnlineAsync(end, ct) - playersBefore;
        }
        catch (PrometheusException)
        {
            // Unknown: a cross-check only.
        }

        StepClientValues client = Merge(settle, judged);
        IReadOnlyDictionary<LimitName, double?> values = Values(server, client, cpu, lag);
        Decision decision = decider.Decide(new StepSample(live.Length, values, cpu));
        Dictionary<BehaviourKind, int> byBehaviour = Enum.GetValues<BehaviourKind>()
            .ToDictionary(kind => kind, kind => live.Count(bot => bot.Behaviour == kind));
        return new StepRecord(index, live.Length, byBehaviour, server, client, cpu, lag, decision)
        {
            InWorld = inWorld,
            PlayersOnlineAdded = playersAdded,
        };
    }

    /// <summary>The judged window's acks with the whole step's entry, sign-in and disconnect counts.</summary>
    private static StepClientValues Merge(StepClientValues settle, StepClientValues judged)
    {
        var failures = new Dictionary<string, int>(settle.EntryFailures, StringComparer.Ordinal);
        foreach ((string kind, int count) in judged.EntryFailures)
            failures[kind] = failures.GetValueOrDefault(kind) + count;

        return judged with
        {
            EntryAttempts = settle.EntryAttempts + judged.EntryAttempts,
            EntryFailures = failures,
            SignInFailures = settle.SignInFailures + judged.SignInFailures,
            Disconnects = settle.Disconnects + judged.Disconnects,
            EntrySuccesses = settle.EntrySuccesses + judged.EntrySuccesses,
            // Percentiles of two windows do not merge: the slower of the two, an upper bound.
            EntryP95 = MaxFinite(settle.EntryP95, judged.EntryP95),
        };
    }

    /// <summary>Every bot signed in leaves the world and signs out, 32 at a time, each on its own timeout.</summary>
    private async Task LeaveAllAsync()
    {
        Bot[] bots;
        lock (_botsLock)
            bots = [.. _bots];

        await Parallel.ForEachAsync(bots, new ParallelOptions { MaxDegreeOfParallelism = Concurrency }, async (bot, _) =>
        {
            using var limit = new CancellationTokenSource(s_leaveTimeout);
            try
            {
                await bot.LeaveAsync(limit.Token);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(Invariant($"  bot {bot.Index} did not leave cleanly: {error.Message}"));
            }
        });
    }

    /// <summary>Disposes every bot signed in, once the leaves, the life loops, the driver and the refresher are done with them.</summary>
    private void DisposeBots()
    {
        lock (_botsLock)
        {
            foreach (Bot bot in _bots) bot.Dispose();
        }
    }

    /// <summary>
    /// Polls the world's players online every 5 s until it is back to <paramref name="before"/>, for up to 90 s (a
    /// closed session's lease, and the 10 s metrics export, lag the leaves). True when it got there.
    /// </summary>
    private static async Task<bool> WaitForDrainAsync(PrometheusClient prometheus, int before)
    {
        using var limit = new CancellationTokenSource(s_drainLimit);
        int? last = null;
        try
        {
            while (true)
            {
                try
                {
                    last = await prometheus.PlayersOnlineAsync(limit.Token);
                    if (last <= before)
                    {
                        Console.WriteLine(Invariant($"The world is back to {last} players online."));
                        return true;
                    }
                }
                catch (PrometheusException)
                {
                    // Tried again at the next poll.
                }

                await Task.Delay(s_drainPoll, limit.Token);
            }
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested)
        {
            Console.Error.WriteLine(Invariant(
                $"The world still had {(last is { } n ? n.ToString(CultureInfo.InvariantCulture) : "an unknown number of")} players online after {s_drainLimit.TotalSeconds:0} s ({before} before the ramp): wait before cleanup."));
            return false;
        }
    }

    /// <summary>The bots in <see cref="_bots"/>, for the driver and the refresher; rebuilt only after a change.</summary>
    private IReadOnlyCollection<Bot> Snapshot()
    {
        IReadOnlyCollection<Bot>? snapshot = Volatile.Read(ref _snapshot);
        if (snapshot is not null) return snapshot;

        lock (_botsLock)
        {
            snapshot = _bots.ToArray();
            Volatile.Write(ref _snapshot, snapshot);
            return snapshot;
        }
    }

    /// <summary>The console line of a step: <c>step 3  bots 150  tick p99 4.2 ms  ack p95 31 ms  ws 22%  → pass</c>.</summary>
    private string StepLine(StepRecord step)
    {
        string arrow = Console.OutputEncoding.CodePage == Encoding.UTF8.CodePage ? "→" : "->";
        return Invariant(
            $"step {step.Index}  bots {step.Bots}  tick p99 {Number(step.Server.TickP99Ms, "0.0")} ms  ack p95 {Number(step.Client.AckP95, "0")} ms  ws {Percent(step.Server.WorkingSetFraction)}  {arrow} {Verdict(step, options.Limits)}");
    }

    /// <summary>A step's verdict in words: pass, a re-hold and why, or the end.</summary>
    public static string Verdict(StepRecord step, IReadOnlyList<Limit> limits)
    {
        Decision decision = step.Decision;
        string breaches = string.Join(", ", decision.Breaches.Select(breach => Describe(breach, limits)));
        string unknowns = string.Join(", ", Unknowns(step, limits).Select(limit => limit.CliName));
        string why = decision.Breaches.Count > 0 ? breaches : unknowns.Length > 0 ? $"unknown: {unknowns}" : "";
        return decision.Action switch
        {
            RampAction.NextStep => decision.Blip ? "pass (blip)" : "pass",
            RampAction.Rehold => $"re-hold ({why})",
            _ => decision.Outcome switch
            {
                RampOutcome.NoLimitReached => "pass, the last step",
                RampOutcome.Unknown => $"stop, unknown ({why})",
                _ => $"stop ({why})",
            },
        };
    }

    /// <summary>A breach as <c>tick-p99 18.2 ms &gt; 16.7 ms</c>.</summary>
    public static string Describe(Breach breach, IReadOnlyList<Limit> limits)
    {
        Limit? limit = limits.FirstOrDefault(l => l.Name == breach.Name);
        string name = limit?.CliName ?? breach.Name.ToString();
        string unit = limit?.Unit ?? "";
        string side = limit is { TripsAbove: false } ? "<" : ">";
        return $"{name} {Format(breach.Value, unit)} {side} {Format(breach.Threshold, unit)}";
    }

    /// <summary>A value in its limit's unit: fractions as percentages.</summary>
    public static string Format(double value, string unit) => unit switch
    {
        "fraction" => Invariant($"{value * 100:0.##} %"),
        "ms" => Invariant($"{value:0.#} ms"),
        "count" => Invariant($"{value:0}"),
        _ => Invariant($"{value:0.##} {unit}"),
    };

    /// <summary>Bots sent into the world that have not given up.</summary>
    private int LiveCount() => _added.Count(bot => bot.State != BotState.Stopped);

    private TimeSpan SignInRate() => _signIns == 0 ? TimeSpan.Zero : _signInTime / _signIns;

    private static int? LastPass(IReadOnlyList<StepRecord> steps) =>
        steps.LastOrDefault(step => step.Decision.Action == RampAction.NextStep ||
            step.Decision.Outcome == RampOutcome.NoLimitReached)?.Bots;

    private static double MaxFinite(double a, double b) =>
        double.IsFinite(a) ? double.IsFinite(b) ? Math.Max(a, b) : a : b;

    private static string Number(double? value, string format) =>
        value is { } v && double.IsFinite(v) ? v.ToString(format, CultureInfo.InvariantCulture) : "n/a";

    private static string Percent(double? fraction) =>
        fraction is { } v && double.IsFinite(v) ? (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : "n/a";

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
