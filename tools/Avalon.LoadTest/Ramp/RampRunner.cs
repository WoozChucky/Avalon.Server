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
/// <param name="Hold">How long each step holds: a settle (30 s, longer for a hold above 90 s), then the judged window (<see cref="RampRunner.JudgedWindow"/>).</param>
/// <param name="Max">The most bots, at most the run's size.</param>
/// <param name="Dial">The host to dial instead of the join reply's, or null.</param>
/// <param name="Prometheus">The Prometheus HTTP API's origin.</param>
/// <param name="Pod">The world server's pod, for its memory limit.</param>
/// <param name="SignInConcurrency">Sign-ins at once, the runner's and the refresher's together.</param>
/// <param name="ForestTime">How long a fighter's trip stays in the forest (<c>--forest-time</c>).</param>
/// <param name="PartySize">The fighters' party size (<c>--party-size</c>), 1 (solo) to <see cref="BotParty.MaxSize"/>.</param>
public sealed record RampOptions(
    string Mix, int Start, int Step, TimeSpan Hold, int Max, IReadOnlyList<Limit> Limits, string? Dial, Uri Prometheus,
    string Pod, int SignInConcurrency, TimeSpan ForestTime, int PartySize);

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

    /// <summary>When the judged window ended, on the bot PC's clock.</summary>
    public DateTimeOffset WindowEnd { get; init; }
}

/// <summary>How much of the check that the world server did not restart during the ramp could be made.</summary>
public enum RestartCheck
{
    /// <summary>The versions, the pod uids, the container's restarts and its start times were all compared.</summary>
    Complete,

    /// <summary>Some of them were.</summary>
    Partial,

    /// <summary>None was: a restart would have gone unseen.</summary>
    Unknown,
}

/// <summary>The reads of the world server at the end of a ramp (<see cref="RampResult.WithEndRead"/>); null where unknown.</summary>
internal readonly record struct EndRead(
    ServerIdentity? Server, int? Restarts, DateTimeOffset? Started, DateTimeOffset? Terminated, DateTimeOffset? LastUp,
    (string Uid, DateTimeOffset Started)? Newest, int? NewestRestarts);

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

    /// <summary>
    /// Sign-in failures over the ramp by kind (<see cref="BotMetrics.SignInFailed"/>), the context refresher's and the
    /// stop sequence's included; identity's side, apart from admission.
    /// </summary>
    public IReadOnlyDictionary<string, int> SignInFailures { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// The bot PC's clock less Prometheus's, measured when the ramp started (positive: the bot PC is ahead); every
    /// query's time was corrected by it.
    /// </summary>
    public TimeSpan ClockOffset { get; init; }

    /// <summary>
    /// The world server's version read at the end: when the ramp ended for the early report, after the bots had left
    /// for the finished one (as every other end read). Null when Prometheus had none.
    /// </summary>
    public string? ServerVersionAtEnd { get; init; }

    /// <summary>The world server's pod uid read before the ramp started (<c>target_info</c>); null when Prometheus had none.</summary>
    public string? ServerPod { get; init; }

    /// <summary>The world server's pod uid read at the end (<c>target_info</c>, which may still name a replaced pod); null when unknown.</summary>
    public string? ServerPodAtEnd { get; init; }

    /// <summary>
    /// The newest pod named by <c>--pod</c> at the end (kube-state-metrics), read only when <c>target_info</c> named that
    /// pod before the ramp: a replacement shows here before the new process exports. Null when unknown.
    /// </summary>
    public string? NewestPod { get; init; }

    /// <summary>When <see cref="NewestPod"/> started (bot PC's clock); null when unknown.</summary>
    public DateTimeOffset? NewestPodStartedAt { get; init; }

    /// <summary>The world server container's restarts in <see cref="NewestPod"/>; null when unknown.</summary>
    public int? NewestPodRestarts { get; init; }

    /// <summary>The world server container's restart count in the pod read before the ramp, read then; null when unknown.</summary>
    public int? ContainerRestartsAtStart { get; init; }

    /// <summary>The same pod's count at the end (its highest over the ramp, so a deleted pod still answers); null when unknown.</summary>
    public int? ContainerRestartsAtEnd { get; init; }

    /// <summary>
    /// How many times the world server's container restarted inside the pod read before the ramp, a restart the pod
    /// uid cannot show; null when either count is unknown or the count fell (a count that cannot be trusted).
    /// </summary>
    public int? ContainerRestarts =>
        ContainerRestartsAtStart is { } start && ContainerRestartsAtEnd is { } end && end >= start ? end - start : null;

    /// <summary>When the world server's container last started, read before the ramp (bot PC's clock); null when unknown.</summary>
    public DateTimeOffset? ContainerStartedAtStart { get; init; }

    /// <summary>
    /// When the world server's container last started, read at the end (bot PC's clock): the newest start of the pod
    /// read before the ramp and of the pods named by <c>--pod</c>. Null when unknown.
    /// </summary>
    public DateTimeOffset? ContainerStartedAtEnd { get; init; }

    /// <summary>When the container in the pod read before the ramp last ended (bot PC's clock); null when unknown or it never did.</summary>
    public DateTimeOffset? ContainerLastTerminatedAt { get; init; }

    /// <summary>
    /// The latest moment the pod read before the ramp was known to be up (its deletion request or its process's last
    /// export; bot PC's clock): its process ended no earlier. Null when unknown.
    /// </summary>
    public DateTimeOffset? OldPodLastUpAt { get; init; }

    /// <summary>When the last judged window ended (bot PC's clock); null when no step was judged.</summary>
    public DateTimeOffset? LastJudgedEnd { get; init; }

    /// <summary>When the ramp ended and the stop sequence began (bot PC's clock); null when unknown.</summary>
    public DateTimeOffset? StopStarted { get; init; }

    /// <summary>
    /// Whether the read after the bots left went ahead without a kube-state-metrics sample taken after the drain ended
    /// (the wait for one ran out): it may show the world as it was before, so the check is partial at best.
    /// </summary>
    public bool KubeNotScrapedSinceDrain { get; init; }

    /// <summary>Whether the world's version read at the end differs from the one read before the ramp (both known).</summary>
    public bool ServerVersionChanged => Differ(ServerVersion, ServerVersionAtEnd);

    /// <summary>
    /// Whether the pod read before the ramp was replaced: <c>target_info</c>'s pod uid at the end, or the newest pod named
    /// by <c>--pod</c>, differs from it (both known).
    /// </summary>
    public bool ServerPodChanged => Differ(ServerPod, ServerPodAtEnd) || Differ(ServerPod, NewestPod);

    /// <summary>Whether the world server's container started again after the start read (both start times known).</summary>
    public bool ContainerStartChanged => ContainerStartedAtStart is { } start && ContainerStartedAtEnd > start;

    /// <summary>Whether the world server restarted at all between the reads before the ramp and at the end, as far as was seen.</summary>
    public bool ServerRestarted => ServerChangeDetails is not null;

    /// <summary>
    /// A lower bound of when the world server that ran the ramp ended: for a replaced pod, the last moment it was known
    /// up (<see cref="OldPodLastUpAt"/>); for a container restart inside it, the container's last end. Null when unknown.
    /// </summary>
    public DateTimeOffset? OldWorldEndedAt => ServerPodChanged ? OldPodLastUpAt : ContainerLastTerminatedAt;

    /// <summary>
    /// Whether the restart is proven to have come after the last judged window ended, so the steps were judged on one
    /// process and the verdict stands. Proven only when the old world ended after that window (<see cref="OldWorldEndedAt"/>
    /// known and later) and exactly one restart is accounted for: a container restart (count 1) in a pod that stayed, or a
    /// pod replaced (no container restart in it) by one that started after the window and has not restarted. Anything
    /// else, unknowns included, is not proven.
    /// </summary>
    public bool RestartedAfterLastJudgedStep =>
        ServerRestarted && LastJudgedEnd is { } judged && OldWorldEndedAt > judged &&
        (ServerPodChanged
            ? ContainerRestarts == 0 && Differ(ServerPod, NewestPod) && NewestPodStartedAt > judged && NewestPodRestarts == 0
            : ContainerRestarts == 1);

    /// <summary>Whether the world server restarted and that it came after the last judged window is not proven: the run does not stand.</summary>
    public bool RestartNotProvenAfter => ServerRestarted && !RestartedAfterLastJudgedStep;

    /// <summary>
    /// Whether the restart is proven to have come before the last judged window ended: the new process started by then
    /// (the newest pod, which under a StatefulSet starts only once the old pod is gone, or the container), or the
    /// container in the pod read before the ramp last ended within the ramp by then (after the ramp started: the series
    /// persists from earlier restarts), whether or not its restart count is known.
    /// </summary>
    public bool RestartProvenDuringRamp =>
        ServerRestarted && LastJudgedEnd is { } judged &&
        ((ServerPodChanged && Differ(ServerPod, NewestPod) && NewestPodStartedAt <= judged) ||
         (ContainerStartChanged && ContainerStartedAtEnd <= judged) ||
         (ContainerLastTerminatedAt is { } ended && ended > Started && ended <= judged));

    /// <summary>
    /// The first fact missing for <see cref="RestartedAfterLastJudgedStep"/> when the restart is neither proven after the
    /// last judged window nor proven before its end; null otherwise.
    /// </summary>
    public string? RestartNotProvenReason
    {
        get
        {
            if (!RestartNotProvenAfter || RestartProvenDuringRamp) return null;
            if (LastJudgedEnd is not { } judged) return "no step was judged";
            if (OldWorldEndedAt is not { } ended)
            {
                return ServerPodChanged ? "when the old pod was last up is unknown"
                    : ContainerRestarts > 0 ? "when the container last ended is unknown"
                    : "when the old process ended is unknown";
            }

            if (!ServerPodChanged && ended <= Started) return "the container's last end predates the ramp";
            if (ended <= judged) return "the old process was last seen up before the last judged window ended";
            if (!ServerPodChanged)
            {
                return ContainerRestarts switch
                {
                    null => "the container's restart count is unknown",
                    0 => "no container restart was counted",
                    _ => FormattableString.Invariant($"{ContainerRestarts} container restarts were counted"),
                };
            }

            if (ContainerRestarts is null) return "the old pod's restart count is unknown";
            if (ContainerRestarts > 0) return "the old pod's container also restarted";
            if (!Differ(ServerPod, NewestPod)) return "the new pod is unknown";
            if (NewestPodStartedAt is null) return "the new pod's start is unknown";
            return NewestPodRestarts is null ? "the new pod's restart count is unknown" : "the new pod's container restarted";
        }
    }

    /// <summary>
    /// Why the run does not stand: the world server restarted and that it came after the last judged window is not
    /// proven, or no restart was seen but the check could not be made whole. Null when the run stands.
    /// </summary>
    public string? DoesNotStandReason => RestartNotProvenAfter
        ? RestartProvenDuringRamp
            ? "the world server restarted during the ramp"
            : $"the world server restarted; not proven after the last judged step ({RestartNotProvenReason})"
        : !ServerRestarted && RestartCheck != RestartCheck.Complete
            ? $"the restart check was {(RestartCheck == RestartCheck.Partial ? "partial" : "unknown")} ({RestartCheckReason})"
            : null;

    /// <summary>Whether the run stands as far as the world server's restarts go (<see cref="DoesNotStandReason"/>).</summary>
    public bool Stands => DoesNotStandReason is null;

    /// <summary>
    /// What showed the world server restarting: <c>A → B; pod x → y; container restarted N times, last started
    /// 18:34:40 UTC</c>, each part only when seen (the version, unchanged, named when the pod or the container shows the
    /// restart); null when nothing did.
    /// </summary>
    public string? ServerChangeDetails
    {
        get
        {
            bool containerRestarted = ContainerRestarts > 0;
            if (!ServerPodChanged && !containerRestarted && !ContainerStartChanged)
                return ServerVersionChanged ? $"{ServerVersion} → {ServerVersionAtEnd}" : null;

            var details = new List<string>(3);
            if (ServerVersionChanged) details.Add($"{ServerVersion} → {ServerVersionAtEnd}");
            else if ((ServerVersion ?? ServerVersionAtEnd) is { } version) details.Add(version);
            if (ServerPodChanged) details.Add($"pod {ServerPod} → {(Differ(ServerPod, NewestPod) ? NewestPod : ServerPodAtEnd)}");
            string? started = ContainerStartChanged && ContainerStartedAtEnd is { } at
                ? at.UtcDateTime.ToString("HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
                : null;
            if (containerRestarted)
            {
                string restarted = ContainerRestarts == 1
                    ? "container restarted once"
                    : FormattableString.Invariant($"container restarted {ContainerRestarts} times");
                details.Add(started is null ? restarted : $"{restarted}, last started {started}");
            }
            else if (started is not null)
            {
                details.Add($"container started {started}");
            }

            return string.Join("; ", details);
        }
    }

    /// <summary>
    /// How the restart came, as a phrase: <c>restarted after the ramp's last judged step, while the bots left</c> (the
    /// old world ended after the stop began), <c>restarted after the ramp's last judged step</c>, <c>restarted during
    /// the ramp</c> (proven before the last judged window ended), or <c>restarted; not proven after the last judged step
    /// (missing fact)</c>; null when no restart was seen.
    /// </summary>
    public string? RestartPhrase => !ServerRestarted
        ? null
        : RestartedAfterLastJudgedStep
            ? StopStarted is { } stop && OldWorldEndedAt > stop
                ? "restarted after the ramp's last judged step, while the bots left"
                : "restarted after the ramp's last judged step"
            : RestartProvenDuringRamp
                ? "restarted during the ramp"
                : $"restarted; not proven after the last judged step ({RestartNotProvenReason})";

    /// <summary>
    /// The world server's restart as the report's header gives it: <c>world</c> and its <see cref="RestartPhrase"/>, then
    /// what showed it (<c>world restarted during the ramp (...)</c>); when only the versions show it, <c>changed during
    /// the ramp: A → B</c> or <c>version changed; not proven after the last judged step (...): A → B</c>. Null when
    /// nothing did.
    /// </summary>
    public string? ServerChange => ServerChangeDetails is not { } details
        ? null
        : RestartedAfterLastJudgedStep || ServerPodChanged || ContainerRestarts > 0 || ContainerStartChanged
            ? RestartProvenDuringRamp || RestartedAfterLastJudgedStep
                ? $"world {RestartPhrase} ({details})"
                : $"world {RestartPhrase}: {details}"
            : RestartProvenDuringRamp
                ? $"changed during the ramp: {details}"
                : $"version changed; not proven after the last judged step ({RestartNotProvenReason}): {details}";

    /// <summary>
    /// How much of the restart check could be made: <see cref="Ramp.RestartCheck.Complete"/> when the versions, the pod
    /// uids, the container's restarts and its start times were all compared, <see cref="Ramp.RestartCheck.Unknown"/>
    /// when none was.
    /// </summary>
    public RestartCheck RestartCheck => Compared switch
    {
        4 when !KubeNotScrapedSinceDrain => RestartCheck.Complete,
        0 => RestartCheck.Unknown,
        _ => RestartCheck.Partial,
    };

    /// <summary>Why the restart check is not <see cref="Ramp.RestartCheck.Complete"/>; null when it is.</summary>
    public string? RestartCheckReason
    {
        get
        {
            if (RestartCheck == RestartCheck.Complete) return null;
            if (ServerVersion is null && ServerPod is null && ContainerRestartsAtStart is null && ContainerStartedAtStart is null)
                return "Prometheus gave nothing before the ramp";
            if (ServerVersionAtEnd is null && ServerPodAtEnd is null && ContainerRestartsAtEnd is null && ContainerStartedAtEnd is null)
                return "Prometheus gave nothing at the end";

            var missing = new List<string>(4);
            if (ServerVersion is null || ServerVersionAtEnd is null) missing.Add("versions not compared");
            if (ServerPod is null || ServerPodAtEnd is null) missing.Add("pod uids not compared");
            if (ContainerRestarts is null)
            {
                missing.Add(ContainerRestartsAtStart is { } start && ContainerRestartsAtEnd < start
                    ? "container restart count fell"
                    : "container restarts not counted");
            }

            if (ContainerStartedAtStart is null || ContainerStartedAtEnd is null) missing.Add("container start times not compared");
            if (KubeNotScrapedSinceDrain) missing.Add("kube-state-metrics not scraped since the drain ended");
            return string.Join(", ", missing);
        }
    }

    private int Compared =>
        (ServerVersion is not null && ServerVersionAtEnd is not null ? 1 : 0) +
        (ServerPod is not null && ServerPodAtEnd is not null ? 1 : 0) +
        (ContainerRestarts is not null ? 1 : 0) +
        (ContainerStartedAtStart is not null && ContainerStartedAtEnd is not null ? 1 : 0);

    private static bool Differ(string? start, string? end) =>
        start is not null && end is not null && !string.Equals(start, end, StringComparison.Ordinal);

    /// <summary>This result with every end read taken from <paramref name="read"/>, unknowns included.</summary>
    internal RampResult WithEndRead(EndRead read) => this with
    {
        ServerVersionAtEnd = read.Server?.Version,
        ServerPodAtEnd = read.Server?.PodUid,
        ContainerRestartsAtEnd = read.Restarts,
        ContainerStartedAtEnd = read.Started,
        ContainerLastTerminatedAt = read.Terminated,
        OldPodLastUpAt = read.LastUp,
        NewestPod = read.Newest?.Uid,
        NewestPodStartedAt = read.Newest?.Started,
        NewestPodRestarts = read.NewestRestarts,
    };

    /// <summary>Failed sign-outs of game contexts over the ramp, its stop sequence's included.</summary>
    public int SignOutFailures { get; init; }

    /// <summary>
    /// Failed leaves, by kind, after the last judged step: the stop sequence's leaves (and any disconnect between the
    /// last hold's end and the stop). Empty while the stop sequence is still running.
    /// </summary>
    public IReadOnlyDictionary<string, int> StopLeaveFailures { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// Whether, after the stop, the world's players online came back to the count before the ramp; null while the stop
    /// sequence is still running (the report written as soon as the outcome is known).
    /// </summary>
    public bool? WorldDrained { get; init; }

    /// <summary>
    /// Sign-outs skipped in the stop sequence (the only time the breaker is armed) because the API was taken as down
    /// (<see cref="SignOutBreaker"/>): those contexts expire within 5 minutes. 0 while the stop sequence still runs.
    /// </summary>
    public int SignOutsSkipped { get; init; }

    /// <summary>
    /// Leaves skipped in the stop sequence (the only time the breaker is armed) because the world was taken as hung
    /// (the leave <see cref="Breaker"/>): their sockets were closed at once. 0 while the stop sequence still runs.
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

    /// <summary>Each party's formation (<see cref="PartyFormer.FormAsync"/>), on the lives' token.</summary>
    private readonly List<Task> _formations = [];

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
    /// <exception cref="PrometheusException">
    /// Prometheus cannot be read before the first bot signs in, or its clock and the bot PC's are more than
    /// <see cref="PrometheusClient.MaxClockOffset"/> apart.
    /// </exception>
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
        // Each step is read for the window that ended at the hold's end on the bot PC's clock: on Prometheus's clock,
        // that is the offset away. Far apart, the clocks are refused rather than trusted.
        TimeSpan clockOffset = await prometheus.MeasureClockOffsetAsync(ct);
        if (clockOffset.Duration() > PrometheusClient.MaxClockOffset)
        {
            throw new PrometheusException(Invariant(
                $"The bot PC's clock is {clockOffset.Duration().TotalSeconds:0} s {(clockOffset > TimeSpan.Zero ? "ahead of" : "behind")} Prometheus's ({options.Prometheus}), more than {PrometheusClient.MaxClockOffset.TotalSeconds:0} s: synchronise the bot PC's clock (on Windows, w32tm /resync) and run again."));
        }

        Console.WriteLine(Invariant(
            $"The bot PC's clock is {ClockOffsetText(clockOffset)} Prometheus's; query times are corrected by it."));
        ServerIdentity? server = await prometheus.ServerAsync(ct);
        string? version = server?.Version;
        if (server?.PodName is { } podName && !string.Equals(podName, options.Pod, StringComparison.Ordinal))
        {
            Console.Error.WriteLine(
                $"World {run.WorldId} runs in pod {podName} (target_info), not --pod {options.Pod}: the working set's share of the memory limit reads --pod's limit.");
        }

        int? restartsBefore = await prometheus.ContainerRestartsAsync(server?.PodUid, over: null, ct);
        DateTimeOffset? startedBefore = await prometheus.ContainerStartedAsync(server?.PodUid, ct);
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
        var signInFailures = new Dictionary<string, int>(StringComparer.Ordinal);
        int signOutFailures = 0;
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
                AddCounts(signInFailures, step.Client.SignInFailures);
                signOutFailures += step.Client.SignOutFailures;
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

        // Read again before the stop sequence (the reads at once, 3 s at most: s_endReadTimeout): a world restarted
        // during the ramp, on any version, invalidates the run. --pod's newest pod stands for the world's only when target_info named that pod before the ramp.
        bool podNamed = server?.PodName is { } named && string.Equals(named, options.Pod, StringComparison.Ordinal);
        DateTimeOffset stopStarted = DateTimeOffset.UtcNow;
        EndRead atEnd = await ReadServerAsync(prometheus, server?.PodUid, podNamed, stopStarted - started, s_endReadTimeout);
        RampResult partial = new RampResult(outcome, capacity, failedFirst, steps, version, SignInRate(), started, DateTimeOffset.UtcNow)
        {
            ServerPod = server?.PodUid,
            ContainerRestartsAtStart = restartsBefore,
            ContainerStartedAtStart = startedBefore,
            LastJudgedEnd = steps.Count == 0 ? null : steps[^1].WindowEnd,
            StopStarted = stopStarted,
            StopReason = stopReason,
            SignIns = _signIns,
            SignInFailures = new Dictionary<string, int>(signInFailures, StringComparer.Ordinal),
            SignOutFailures = signOutFailures,
            WorldDrained = null,
            ClockOffset = clockOffset,
        }.WithEndRead(atEnd);
        if (partial.ServerChange is { } change)
        {
            Console.Error.WriteLine(partial.Stands
                ? $"World {run.WorldId} server: {change}. The verdict stands."
                : $"World {run.WorldId} server: {change}. This run does not stand.");
        }

        try
        {
            outcomeKnown?.Invoke(partial);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Writing the report before the stop failed: {error.Message}");
        }

        Console.WriteLine(Invariant($"Stopping: {_added.Count} bots leave the world."));
        // The breakers act only from here: during the ramp every leave and sign-out ran, its failures counted.
        _leaves.Arm();
        _signOuts.Arm();
        await presigning.CancelAsync();
        if (presign is not null) await presign.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        await lives.CancelAsync();
        await Task.WhenAll(_lives).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        // No party request is under way when the bots leave.
        await Task.WhenAll(_formations).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
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
            Console.Error.WriteLine(Invariant($"World unresponsive during the stop: {_leaves.Skipped} leaves skipped; sockets closed."));
        }

        if (_signOuts.Skipped > 0)
        {
            Console.Error.WriteLine(Invariant(
                $"API down during the stop: {_signOuts.Skipped} sign-outs skipped; those contexts expire within 5 minutes."));
        }

        StepClientValues last = metrics.TakeWindow();
        AddCounts(signInFailures, last.SignInFailures);
        signOutFailures += last.SignOutFailures;
        bool drained = await WaitForDrainAsync(prometheus, playersBefore);
        // A restart late in the ramp, or during the stop, may show only now: the finished report's end reads all come
        // from here, none falling back to the earlier read (an unknown stays unknown, and proves nothing). A replaced
        // pod drains at once (its players online start at 0), so target_info may still name the old process here; the
        // kube-state-metrics reads (by uid and by --pod) show the new one, once scraped after the drain ended.
        bool scraped = await WaitForKubeScrapeAsync(prometheus, server?.PodUid, DateTimeOffset.UtcNow);
        EndRead final = await ReadServerAsync(prometheus, server?.PodUid, podNamed, DateTimeOffset.UtcNow - started, s_finalReadTimeout);

        // The count and the rate from one moment: sign-ins still landing when the outcome was known count in both.
        RampResult result = partial.WithEndRead(final) with
        {
            KubeNotScrapedSinceDrain = !scraped,
            SignIns = _signIns,
            SignInRate = SignInRate(),
            Ended = DateTimeOffset.UtcNow,
            SignInFailures = signInFailures,
            SignOutFailures = signOutFailures,
            StopLeaveFailures = last.LeaveFailures,
            WorldDrained = drained,
            SignOutsSkipped = _signOuts.Skipped,
            LeavesSkipped = _leaves.Skipped,
        };
        if (result.ServerChange is { } finalChange && !string.Equals(finalChange, partial.ServerChange, StringComparison.Ordinal))
        {
            Console.Error.WriteLine(result.Stands
                ? $"World {run.WorldId} server: {finalChange}. The verdict stands."
                : $"World {run.WorldId} server: {finalChange}. This run does not stand.");
        }

        return result;
    }

    /// <summary>The longest the reads of the world server at the ramp's end, for the early report, may take together.</summary>
    private static readonly TimeSpan s_endReadTimeout = TimeSpan.FromSeconds(3);

    /// <summary>The longest the deciding reads after the bots left may take together.</summary>
    private static readonly TimeSpan s_finalReadTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The longest the runner waits for kube-state-metrics to be scraped after the drain, before the last read.</summary>
    private static readonly TimeSpan s_kubeScrapeWait = TimeSpan.FromSeconds(45);

    /// <summary>
    /// The world server as <see cref="RampResult"/>'s end reads take it, read at once and bounded by
    /// <paramref name="limit"/> rather than the ramp's token (a Ctrl+C has cancelled it); a read that did not answer in
    /// time is null. <paramref name="podUid"/> is the pod read before the ramp; <paramref name="podNamed"/> whether
    /// <c>--pod</c> named it, so its newest pod stands for the world's; <paramref name="ramp"/> how far back the reads of
    /// a pod deleted since look.
    /// </summary>
    private static async Task<EndRead> ReadServerAsync(PrometheusClient prometheus, string? podUid, bool podNamed, TimeSpan ramp,
        TimeSpan limit)
    {
        using var timeout = new CancellationTokenSource(limit);
        CancellationToken token = timeout.Token;
        TimeSpan lookBack = ramp + TimeSpan.FromMinutes(5);
        Task<ServerIdentity?> server = prometheus.ServerAsync(token);
        Task<int?> restarts = prometheus.ContainerRestartsAsync(podUid, lookBack, token);
        Task<DateTimeOffset?> started = prometheus.ContainerStartedAsync(podUid, token);
        Task<DateTimeOffset?> terminated = podUid is null ? Task.FromResult<DateTimeOffset?>(null) : prometheus.ContainerLastTerminatedAsync(podUid, token);
        Task<DateTimeOffset?> lastUp = podUid is null ? Task.FromResult<DateTimeOffset?>(null) : prometheus.PodLastUpAsync(podUid, lookBack, token);
        Task<(string Uid, DateTimeOffset Started)?> newest = podNamed
            ? prometheus.NewestPodAsync(token)
            : Task.FromResult<(string Uid, DateTimeOffset Started)?>(null);
        int? newestRestarts = null;
        try
        {
            await Task.WhenAll(server, restarts, started, terminated, lastUp, newest);
            if (newest.Result is { } pod) newestRestarts = await prometheus.ContainerRestartsAsync(pod.Uid, over: null, token);
        }
        catch (OperationCanceledException)
        {
            // Whichever read ran out of time is unknown.
        }

        return new EndRead(Done(server), Done(restarts), Done(started), Done(terminated), Done(lastUp), Done(newest), newestRestarts);

        static T? Done<T>(Task<T> task) => task.IsCompletedSuccessfully ? task.Result : default;
    }

    /// <summary>
    /// Waits, at most <see cref="s_kubeScrapeWait"/>, until kube-state-metrics' container start for the world has a sample
    /// taken after <paramref name="after"/>, so the last read does not see the world as it was before the drain ended.
    /// False when the wait ran out.
    /// </summary>
    private static async Task<bool> WaitForKubeScrapeAsync(PrometheusClient prometheus, string? podUid, DateTimeOffset after)
    {
        DateTimeOffset deadline = after + s_kubeScrapeWait;
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var timeout = new CancellationTokenSource(s_endReadTimeout);
            try
            {
                if (await prometheus.ContainerStartScrapedAsync(podUid, timeout.Token) > after) return true;
            }
            catch (OperationCanceledException)
            {
                // Not yet known; try again.
            }

            await Task.Delay(TimeSpan.FromSeconds(5));
        }

        return false;
    }

    /// <summary>
    /// The limits' values for a step: the server's, the bots' and the bot PC's. Admission is the bots that tried to
    /// enter in the step and never got in there ÷ the bots that tried (<see cref="StepClientValues.BotsFailing"/>,
    /// <see cref="StepClientValues.BotsTried"/>), each bot once however many attempts it made; 0 when none tried.
    /// </summary>
    public static IReadOnlyDictionary<LimitName, double?> Values(ServerValues server, StepClientValues client, double generatorCpu,
        double generatorLagP95Ms)
    {
        return new Dictionary<LimitName, double?>
        {
            [LimitName.TickP99] = server.TickP99Ms,
            [LimitName.Tps] = server.Tps,
            [LimitName.AckP95] = client.AckP95,
            [LimitName.Drops] = server.Drops,
            [LimitName.Admission] = client.BotsTried == 0 ? 0 : (double)client.BotsFailing / client.BotsTried,
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
    /// <remarks>
    /// With <c>--party-size</c> N above 1, the fighters among the bots entered are grouped in index order into whole
    /// parties of N, which form in the background once their members are in (<see cref="PartyFormer"/>); the fighters
    /// left over, fewer than N, fight solo. The mix decides every bot's behaviour as without parties, so the count
    /// entered and the shares of the mix stay exact.
    /// </remarks>
    private async Task<int> FillAsync(int target, ApiClient api, BotMetrics metrics, SemaphoreSlim signIns,
        CancellationToken life, CancellationToken ct)
    {
        int need = target - LiveCount();
        if (need <= 0) return 0;

        if (_ready.Count < need) await SignInAsync(need - _ready.Count, api, metrics, signIns, ct);

        var entering = new List<Bot>();
        while (entering.Count < need && _ready.TryDequeue(out Bot? bot)) entering.Add(bot);
        if (entering.Count == 0) return 0;

        // Before the entries: a fighter's connection keeps its table, and a party member's its roster, from the first
        // packet on.
        List<BotParty> parties = Fighters(entering, metrics);
        string inParties = parties.Count == 0 ? "" : Invariant($", {parties.Count} parties of {options.PartySize}");
        Console.Error.WriteLine(Invariant($"  entering {entering.Count} bots ({LiveCount() + entering.Count} live{inParties})"));
        await Parallel.ForEachAsync(entering, new ParallelOptions { MaxDegreeOfParallelism = Concurrency, CancellationToken = ct },
            async (bot, token) =>
            {
                bool failed = false;
                try
                {
                    await bot.EnterAsync(takeover: false, token);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    // Counted by the bot; its life enters it again, after the pause.
                    failed = true;
                }

                lock (_botsLock)
                {
                    _added.Add(bot);
                    _lives.Add(BotLife.RunAsync(bot, metrics, life, pauseFirst: failed));
                }
            });

        // Their members stand in town until each party has formed or gone solo.
        foreach (BotParty party in parties) _formations.Add(PartyFormer.FormAsync(party, life));
        return entering.Count;
    }

    /// <summary>
    /// Gives each bot its behaviour from the mix, and each fighter its <see cref="Fighter"/>: in whole parties of
    /// <c>--party-size</c>, in index order, setting out together once formed; the rest, fewer than a party, solo after
    /// their own first-trip wait. Returns the parties, still to be formed.
    /// </summary>
    private List<BotParty> Fighters(List<Bot> entering, BotMetrics metrics)
    {
        var fighters = new List<Bot>();
        foreach (Bot bot in entering)
        {
            bot.Behaviour = Bots.Mix.For(bot.Index, _mix);
            if (bot.Behaviour == BehaviourKind.Fighter) fighters.Add(bot);
        }

        int size = options.PartySize;
        int inParties = size > 1 ? fighters.Count / size * size : 0;
        var parties = new List<BotParty>(inParties / Math.Max(size, 1));
        for (int i = 0; i < fighters.Count; i++)
        {
            Bot bot = fighters[i];
            // A party waits out one first-trip wait for all its members (BotParty), so they set out together.
            bot.Fighter = new Fighter(bot.Index, metrics, options.ForestTime, i < inParties ? TimeSpan.Zero : Fighter.FirstTripJitter);
        }

        for (int first = 0; first < inParties; first += size)
        {
            Bot[] members = [.. fighters.GetRange(first, size)];
            var party = new BotParty(members, metrics, Fighter.FirstTripJitter);
            for (int i = 0; i < members.Length; i++)
            {
                members[i].Party = party.Links[i];
                members[i].Fighter!.ReadyToLeaveTown = party.Links[i].ReadyToLeave;
            }

            parties.Add(party);
        }

        return parties;
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
    /// Prometheus are read and the decider asked. The settle's entry, leave, sign-in, sign-out and disconnect counts
    /// count towards the step; its acks do not.
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
            WindowEnd = end,
        };
    }

    /// <summary>
    /// The judged window's acks with the whole step's entry, leave, sign-in, sign-out and disconnect counts, and its fighter
    /// counts.
    /// </summary>
    private static StepClientValues Merge(StepClientValues settle, StepClientValues judged)
    {
        var failures = new Dictionary<string, int>(settle.EntryFailures, StringComparer.Ordinal);
        AddCounts(failures, judged.EntryFailures);
        var leaveFailures = new Dictionary<string, int>(settle.LeaveFailures, StringComparer.Ordinal);
        AddCounts(leaveFailures, judged.LeaveFailures);
        var signInFailures = new Dictionary<string, int>(settle.SignInFailures, StringComparer.Ordinal);
        AddCounts(signInFailures, judged.SignInFailures);
        var castsRefused = new Dictionary<string, int>(settle.CastsRefused, StringComparer.Ordinal);
        AddCounts(castsRefused, judged.CastsRefused);
        var fighterFailures = new Dictionary<string, int>(settle.FighterFailures, StringComparer.Ordinal);
        AddCounts(fighterFailures, judged.FighterFailures);
        var partyFailures = new Dictionary<string, int>(settle.PartyFormFailures, StringComparer.Ordinal);
        AddCounts(partyFailures, judged.PartyFormFailures);

        return judged with
        {
            EntryAttempts = settle.EntryAttempts + judged.EntryAttempts,
            EntryFailures = failures,
            SignInFailures = signInFailures,
            LeaveFailures = leaveFailures,
            SignOutFailures = settle.SignOutFailures + judged.SignOutFailures,
            Disconnects = settle.Disconnects + judged.Disconnects,
            EntrySuccesses = settle.EntrySuccesses + judged.EntrySuccesses,
            // The step's bots, settle and judged window together: one that failed in the settle and got in later in
            // the step got in.
            TriedBots = settle.TriedBots.Union(judged.TriedBots).ToHashSet(),
            GotInBots = settle.GotInBots.Union(judged.GotInBots).ToHashSet(),
            // Percentiles of two windows do not merge: the slower of the two, an upper bound.
            EntryP95 = MaxFinite(settle.EntryP95, judged.EntryP95),
            ForestEntries = settle.ForestEntries + judged.ForestEntries,
            ForestEntryP50 = MaxFinite(settle.ForestEntryP50, judged.ForestEntryP50),
            ForestEntryP95 = MaxFinite(settle.ForestEntryP95, judged.ForestEntryP95),
            ForestTrips = settle.ForestTrips + judged.ForestTrips,
            CastsSent = settle.CastsSent + judged.CastsSent,
            CastsRefused = castsRefused,
            Kills = settle.Kills + judged.Kills,
            OwnDeaths = settle.OwnDeaths + judged.OwnDeaths,
            FighterFailures = fighterFailures,
            PartiesFormed = settle.PartiesFormed + judged.PartiesFormed,
            PartyFormFailures = partyFailures,
        };
    }

    /// <summary>Adds <paramref name="counts"/> into <paramref name="total"/>, kind by kind.</summary>
    private static void AddCounts(Dictionary<string, int> total, IReadOnlyDictionary<string, int> counts)
    {
        foreach ((string kind, int count) in counts)
            total[kind] = total.GetValueOrDefault(kind) + count;
    }

    /// <summary>A clock offset in words: <c>0.4 s ahead of</c>, <c>1.2 s behind</c>.</summary>
    public static string ClockOffsetText(TimeSpan offset) =>
        Invariant($"{offset.Duration().TotalSeconds:0.0} s {(offset >= TimeSpan.Zero ? "ahead of" : "behind")}");

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

    /// <summary>
    /// The console line of a step: <c>step 3  bots 150  tick p99 4.2 ms  ack p95 31 ms  ws 22%  → pass</c>; with fighters,
    /// their count, forest instances per tick and trips before the verdict (<c>fighters 30  forest 9.8  trips 12</c>).
    /// </summary>
    private string StepLine(StepRecord step)
    {
        string arrow = Console.OutputEncoding.CodePage == Encoding.UTF8.CodePage ? "→" : "->";
        int fighters = step.ByBehaviour.GetValueOrDefault(BehaviourKind.Fighter);
        string fighting = fighters == 0
            ? ""
            : Invariant($"  fighters {fighters}  forest {Number(step.Server.InstancesByMap.Forest, "0.0")}  trips {step.Client.ForestTrips}");
        return Invariant(
            $"step {step.Index}  bots {step.Bots}  tick p99 {Number(step.Server.TickP99Ms, "0.0")} ms  ack p95 {Number(step.Client.AckP95, "0")} ms  ws {Percent(step.Server.WorkingSetFraction)}{fighting}  {arrow} {Verdict(step, options.Limits)}");
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
