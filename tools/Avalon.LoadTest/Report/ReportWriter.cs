using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalon.LoadTest.Bots;
using Avalon.LoadTest.Ramp;
using Avalon.LoadTest.Runs;

namespace Avalon.LoadTest.Report;

/// <summary>
/// A ramp's report, as Markdown to read and JSON with the same data: the settings, the result and what failed first,
/// one row per step, and notes (blips, slow kicks that may be the bots' own, identity's sign-in rate). Kept in
/// <see cref="Directory"/>, never in the repository, and holding no secret: no password, ticket or credential.
/// </summary>
public static class ReportWriter
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        // Percentiles with no sample are NaN.
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary><c>%LOCALAPPDATA%\Avalon.LoadTest\reports</c> (on Linux <c>~/.local/share/Avalon.LoadTest/reports</c>, on macOS <c>~/Library/Application Support/Avalon.LoadTest/reports</c>).</summary>
    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Avalon.LoadTest", "reports");

    /// <summary>The report's Markdown and JSON.</summary>
    public static (string Markdown, string Json) Write(RampResult result, RampOptions options, RunFile run)
    {
        string cpu = CpuModel();
        return (Markdown(result, options, run, cpu), Json(result, options, run, cpu));
    }

    /// <summary>
    /// Writes the report as <c>.md</c> and <c>.json</c>. With no <paramref name="markdownPath"/>, to a new
    /// <c>&lt;yyyyMMdd-HHmmss&gt;-&lt;runId&gt;</c> in <see cref="Directory"/> (a <c>-2</c>, <c>-3</c>, ... suffix
    /// rather than overwrite any report there); with one, over that report, this ramp's own written earlier (the one
    /// saved when its outcome was known, completed after the stop). Each file is written whole or not at all: an earlier
    /// version stays until the new one replaces it. Returns the Markdown's path.
    /// </summary>
    public static string Save(RampResult result, RampOptions options, RunFile run, string? markdownPath = null)
    {
        (string markdown, string json) = Write(result, options, run);
        System.IO.Directory.CreateDirectory(Directory);
        string stem;
        if (markdownPath is not null)
        {
            stem = Path.ChangeExtension(markdownPath, null);
        }
        else
        {
            string name = $"{result.Started.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{run.RunId}";
            stem = Path.Combine(Directory, name);
            for (int n = 2; File.Exists(stem + ".md") || File.Exists(stem + ".json"); n++)
                stem = Path.Combine(Directory, $"{name}-{n.ToString(CultureInfo.InvariantCulture)}");
        }

        Replace(stem + ".md", markdown);
        Replace(stem + ".json", json);
        return stem + ".md";
    }

    /// <summary>
    /// Writes <paramref name="path"/> through <c>&lt;path&gt;.tmp</c> and a move over it, so a process ended part way
    /// (a second Ctrl+C during the rewrite) leaves the earlier report whole rather than a truncated one.
    /// </summary>
    private static void Replace(string path, string text)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, text);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>The result in a line: <c>capacity 300 bots</c>, <c>bot PC saturated: capacity ≥ 450 bots</c>, ...</summary>
    public static string ResultLine(RampResult result) => Verdict(result) +
        (result.DoesNotStandReason is { } reason ? $"; does not stand: {reason}" : "");

    private static string Verdict(RampResult result) => result.Outcome switch
    {
        RampOutcome.Capacity => Invariant($"capacity {result.Capacity ?? 0} bots"),
        RampOutcome.NoLimitReached => Invariant($"no limit reached up to {result.Capacity ?? 0} bots") +
            (result.StopReason is { } reason ? $" ({reason})" : ""),
        RampOutcome.GeneratorSaturated => Invariant($"bot PC saturated: capacity ≥ {result.Capacity ?? 0} bots"),
        RampOutcome.Unknown => "stopped: steps that could not be judged" + AtLeast(result.Capacity),
        _ => $"stopped ({result.StopReason ?? "unknown reason"})" + AtLeast(result.Capacity),
    };

    private static string AtLeast(int? capacity) =>
        capacity is { } n ? Invariant($"; the last passing step held {n} bots") : "; no step passed";

    private static string Markdown(RampResult result, RampOptions options, RunFile run, string cpu)
    {
        var md = new StringBuilder();
        md.AppendLine(Invariant($"# Load-test ramp: world {run.WorldId}, run {run.RunId}"));
        md.AppendLine();
        md.AppendLine(Invariant($"- Date: {result.Started.ToLocalTime():yyyy-MM-dd HH:mm zzz} to {result.Ended.ToLocalTime():HH:mm zzz}"));
        md.AppendLine($"- World {run.WorldId} server version: {ServerVersionText(result)}");
        md.AppendLine(Invariant($"- Run: {run.RunId} ({run.Bots.Count} bots) through {run.Api}"));
        md.AppendLine($"- Mix: {options.Mix}");
        md.AppendLine($"- Fighters: {FightersText(options)}");
        md.AppendLine(Invariant(
            $"- Ramp: start {options.Start}, step {options.Step}, hold {options.Hold.TotalSeconds:0} s (judged on the last {RampRunner.JudgedWindow(options.Hold).TotalSeconds:0} s), max {options.Max}, {options.SignInConcurrency} sign-ins at once"));
        md.AppendLine($"- Prometheus: {options.Prometheus} (pod {options.Pod}){(options.Dial is { } dial ? $"; dialling {dial}" : "")}");
        md.AppendLine(Invariant($"- Bot PC: {cpu}, {Environment.ProcessorCount} logical cores; its clock {RampRunner.ClockOffsetText(result.ClockOffset)} Prometheus's at the start (query times corrected by it)"));
        md.AppendLine();
        md.AppendLine("| Limit | Trips when | |");
        md.AppendLine("|---|---|---|");
        foreach (Limit limit in options.Limits)
        {
            Limit standard = Limits.Defaults.First(l => l.Name == limit.Name);
            string overridden = standard.Threshold.Equals(limit.Threshold) ? "" : "overridden";
            md.AppendLine($"| {limit.CliName} | {(limit.TripsAbove ? ">" : "<")} {RampRunner.Format(limit.Threshold, limit.Unit)} | {overridden} |");
        }

        md.AppendLine();
        md.AppendLine("## Result");
        md.AppendLine();
        md.AppendLine($"**{ResultLine(result)}**");
        if (result.FailedFirst.Count > 0)
        {
            md.AppendLine();
            md.AppendLine("Failed first:");
            md.AppendLine();
            foreach (Breach breach in result.FailedFirst)
                md.AppendLine($"- {RampRunner.Describe(breach, options.Limits)}");
        }

        md.AppendLine();
        md.AppendLine("## Steps");
        md.AppendLine();
        md.AppendLine("| Step | Live bots (idle / walker / churner / fighter) | In world at hold end | Players online − start | Instances (all maps) | Tick p99 | TPS | Ack p50 / p95 / p99 | Slow kicks | Receive backlog | Send threads busy: cores (busiest thread) | Pending bytes p99 | Working set | GC stall | GC pause | Gen2 / min (not judged) | Save p95 | Admission: bots failing / tried | Entries / failed | Failures by kind | Leave failures (not admission) | Sign-in failures (not admission) | Disconnects | Bot PC CPU | Driver lateness p95 | Verdict |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (StepRecord step in result.Steps)
        {
            ServerValues s = step.Server;
            StepClientValues c = step.Client;
            int failed = c.EntryFailures.Values.Sum();
            string kinds = Kinds(c.EntryFailures);
            string leaves = Kinds(c.LeaveFailures);
            string signIns = Kinds(c.SignInFailures);
            string bots = Invariant(
                $"{step.Bots} ({step.ByBehaviour.GetValueOrDefault(BehaviourKind.Idle)} / {step.ByBehaviour.GetValueOrDefault(BehaviourKind.Walker)} / {step.ByBehaviour.GetValueOrDefault(BehaviourKind.Churner)} / {step.ByBehaviour.GetValueOrDefault(BehaviourKind.Fighter)})");
            string workingSet = s.WorkingSetMb is { } mb
                ? Invariant($"{mb:0} MB ({Percent(s.WorkingSetFraction)})")
                : "n/a";
            md.AppendLine(string.Join(" | ",
                "| " + step.Index.ToString(CultureInfo.InvariantCulture),
                bots,
                step.InWorld.ToString(CultureInfo.InvariantCulture),
                step.PlayersOnlineAdded is { } added ? added.ToString(CultureInfo.InvariantCulture) : "n/a",
                Number(s.Instances, "0"),
                Ms(s.TickP99Ms, "0.0"),
                Number(s.Tps, "0.0"),
                double.IsFinite(c.AckP95) ? $"{Number(c.AckP50, "0")} / {Number(c.AckP95, "0")} / {Number(c.AckP99, "0")} ms" : "n/a",
                s.SlowKicks.Readout == SendReadout.NotExported ? "not exported" : Number(s.SlowKicks.Count, "0"),
                Number(s.ReceiveBacklogMax, "0"),
                SendBusy(s.SendThreads),
                s.SendThreads.Readout == SendReadout.NotExported ? "not exported" : Bytes(s.SendThreads.PendingBytesP99),
                workingSet,
                s.GcStall.Readout == GcStallReadout.NotExported ? "not exported" : Ms(s.GcStall.Ms, "0.0"),
                Percent(s.GcPauseFraction, "0.##"),
                Number(s.Gen2PerMin, "0.##"),
                Ms(s.SaveP95Ms, "0"),
                Invariant($"{c.BotsFailing} / {c.BotsTried}"),
                Invariant($"{c.EntryAttempts} / {failed}"),
                kinds,
                leaves,
                signIns,
                c.Disconnects.ToString(CultureInfo.InvariantCulture),
                Percent(step.GeneratorCpu),
                Ms(step.GeneratorLagP95Ms, "0.0"),
                RampRunner.Verdict(step, options.Limits) + " |"));
        }

        FightersTable(md, result.Steps);
        PostUpdateTable(md, result.Steps);

        md.AppendLine();
        md.AppendLine("## Notes");
        md.AppendLine();
        if (result.ServerChangeDetails is { } serverChange && result.RestartedAfterLastJudgedStep)
        {
            md.AppendLine($"- **The world server {result.RestartPhrase} ({serverChange}).** The steps were judged on one process, so the verdict stands; check the world before the next run.");
        }
        else if (result.ServerChange is { } change)
        {
            md.AppendLine($"- {char.ToUpperInvariant(change[0])}{change[1..]}. This run does not stand. Run again.");
        }
        else if (result.RestartCheck != RestartCheck.Complete)
        {
            md.AppendLine($"- Restart check {RestartCheckText(result.RestartCheck)} ({result.RestartCheckReason}): whether the world server restarted during the ramp is not known, so this run does not stand. Run again.");
        }

        StepRecord[] blips = [.. result.Steps.Where(step => step.Decision.Blip)];
        md.AppendLine(blips.Length == 0
            ? "- Blips: none."
            : $"- Blips (a breach that passed its re-hold): steps {string.Join(", ", blips.Select(step => step.Index.ToString(CultureInfo.InvariantCulture)))}.");
        StepRecord[] slowReaders = [.. result.Steps.Where(step => step.Decision.SlowKicksMayBeGenerator)];
        if (slowReaders.Length > 0)
        {
            md.AppendLine($"- Slow kicks while the bot PC was above 60 % CPU, possibly the bots reading slowly rather than the server: steps {string.Join(", ", slowReaders.Select(step => step.Index.ToString(CultureInfo.InvariantCulture)))}.");
        }

        StepRecord[] gcStallNotExported = [.. result.Steps.Where(step => step.Server.GcStall.Readout == GcStallReadout.NotExported)];
        if (gcStallNotExported.Length > 0)
        {
            md.AppendLine($"- GC pause time or collections not exported by this world build: gc-stall was not judged on steps {string.Join(", ", gcStallNotExported.Select(step => step.Index.ToString(CultureInfo.InvariantCulture)))}.");
        }

        StepRecord[] sendNotExported = [.. result.Steps.Where(step => step.Server.SlowKicks.Readout == SendReadout.NotExported)];
        if (sendNotExported.Length > 0)
        {
            md.AppendLine($"- Send passes not exported by this world build (one from before the send threads, #875): slow-kicks was not judged on steps {string.Join(", ", sendNotExported.Select(step => step.Index.ToString(CultureInfo.InvariantCulture)))}.");
        }

        int signInFailures = result.SignInFailures.Values.Sum();
        string signInKinds = signInFailures == 0 ? "" : $" ({Kinds(result.SignInFailures)})";
        md.AppendLine(result.SignIns == 0
            ? Invariant($"- Sign-ins (identity, apart from the world): none; {signInFailures} sign-in or refresh failures{signInKinds}.")
            : Invariant(
                $"- Sign-ins (identity, apart from the world): {result.SignIns} bots, one every {result.SignInRate.TotalSeconds:0.00} s with {options.SignInConcurrency} at once ({60 / result.SignInRate.TotalSeconds:0} per minute); {signInFailures} sign-in or refresh failures{signInKinds}."));
        md.AppendLine(result.WorldDrained switch
        {
            true => "- After the stop the world's players online came back to the count before the ramp.",
            false => "- After the stop the world's players online had not come back to the count before the ramp within 90 s: wait before cleanup.",
            null => "- World drained: pending. The bots were still leaving when this was written; the report is rewritten when they are gone.",
        });
        md.AppendLine(Invariant($"- Sign-outs of game contexts that failed: {result.SignOutFailures}."));
        if (result.StopLeaveFailures.Count > 0)
        {
            md.AppendLine($"- Leaves that failed after the last judged step (the stop's): {Kinds(result.StopLeaveFailures)}.");
        }

        if (result.LeavesSkipped > 0)
        {
            md.AppendLine(Invariant($"- World unresponsive during the stop: {result.LeavesSkipped} leaves skipped; sockets closed."));
        }

        if (result.SignOutsSkipped > 0)
        {
            md.AppendLine(Invariant(
                $"- API down during the stop: {result.SignOutsSkipped} sign-outs skipped; those contexts expire within 5 minutes."));
        }

        return md.ToString();
    }

    /// <summary>
    /// The header's fighter settings: <c>--forest-time</c> and <c>--party-size</c>, and whether the mix has fighters at
    /// all (the settings then apply to none).
    /// </summary>
    private static string FightersText(RampOptions options)
    {
        string parties = options.PartySize == 1
            ? "--party-size 1 (solo)"
            : Invariant($"--party-size {options.PartySize} (the fighters a step adds beyond whole parties fight solo)");
        string settings = Invariant($"--forest-time {options.ForestTime.TotalSeconds:0} s, {parties}");
        bool inMix = Mix.Parse(options.Mix).Any(entry => entry.Kind == BehaviourKind.Fighter && entry.Weight > 0);
        return inMix
            ? Invariant($"{settings}; a fighter's first trip (a party's, together) waits a random 0 to {Fighter.FirstTripJitter.TotalSeconds:0} s")
            : $"none in the mix ({settings})";
    }

    /// <summary>
    /// Each step's fighters: how many, the instances the world ticked by map type, their forest entries (portal request
    /// to transition), trips, casts sent and refused by reason, kills, deaths, failed trip steps by kind, and parties
    /// formed and failed by reason. Over the whole step, settle included. For reading only: no limit is judged on them.
    /// When no step held a fighter, the section says so in one line.
    /// </summary>
    private static void FightersTable(StringBuilder md, IReadOnlyList<StepRecord> steps)
    {
        md.AppendLine();
        md.AppendLine("## Fighters");
        md.AppendLine();
        if (!steps.Any(step => step.ByBehaviour.GetValueOrDefault(BehaviourKind.Fighter) > 0))
        {
            md.AppendLine("None: no step held a fighter.");
            return;
        }

        md.AppendLine("Over each whole step, settle included; entry times are the slower of the settle's and the judged window's. Instances are the mean the world ticked per tick over the judged window. Not a limit.");
        md.AppendLine();
        md.AppendLine("| Step | Fighters | Instances per tick: town / forest | Forest entries | Entry p50 / p95 | Trips completed | Casts sent | Casts refused (by reason) | Kills seen | Own deaths | Trip failures by kind | Parties formed | Party failures by reason |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (StepRecord step in steps)
        {
            StepClientValues c = step.Client;
            InstancesByMap instances = step.Server.InstancesByMap;
            int refused = c.CastsRefused.Values.Sum();
            md.AppendLine(string.Join(" | ",
                "| " + step.Index.ToString(CultureInfo.InvariantCulture),
                step.ByBehaviour.GetValueOrDefault(BehaviourKind.Fighter).ToString(CultureInfo.InvariantCulture),
                instances.Readout switch
                {
                    InstancesReadout.Reported => $"{Number(instances.Town, "0.0")} / {Number(instances.Forest, "0.0")}",
                    InstancesReadout.NotExported => "not exported by this world build",
                    _ => "n/a",
                },
                c.ForestEntries.ToString(CultureInfo.InvariantCulture),
                double.IsFinite(c.ForestEntryP50) || double.IsFinite(c.ForestEntryP95)
                    ? $"{Number(c.ForestEntryP50, "0")} / {Number(c.ForestEntryP95, "0")} ms"
                    : "n/a",
                c.ForestTrips.ToString(CultureInfo.InvariantCulture),
                c.CastsSent.ToString(CultureInfo.InvariantCulture),
                refused == 0 ? "0" : Invariant($"{refused} ({Kinds(c.CastsRefused)})"),
                c.Kills.ToString(CultureInfo.InvariantCulture),
                c.OwnDeaths.ToString(CultureInfo.InvariantCulture),
                Kinds(c.FighterFailures),
                c.PartiesFormed.ToString(CultureInfo.InvariantCulture),
                Kinds(c.PartyFormFailures) + " |"));
        }
    }

    /// <summary>
    /// Each step's time after the world update by stage (<c>world.post_update.duration</c>, #875): one row per step, one
    /// column per stage in tick order, mean / p99 per tick in microseconds. For reading only: no limit is judged on it.
    /// When no step was reported and at least one came from a world that exports no stage (a build before #875), the
    /// section says so in one line rather than showing rows of n/a.
    /// </summary>
    private static void PostUpdateTable(StringBuilder md, IReadOnlyList<StepRecord> steps)
    {
        md.AppendLine();
        md.AppendLine("## Post-update stages");
        md.AppendLine();
        if (steps.Any(step => step.Server.PostUpdate.Readout == PostUpdateReadout.NotExported) &&
            !steps.Any(step => step.Server.PostUpdate.Readout == PostUpdateReadout.Reported))
        {
            md.AppendLine("Not reported: on every step Prometheus could read, the world server exported no `world.post_update.duration` (a build from before #875).");
            return;
        }

        string[] stages = [.. PostUpdateStages.InTickOrder(steps.SelectMany(step => step.Server.PostUpdate.Stages).Select(timing => timing.Stage))];
        if (stages.Length == 0)
        {
            md.AppendLine("Not reported: Prometheus gave no stage for any step.");
            return;
        }

        md.AppendLine("The time of each stage after the world update, mean / p99 per tick in µs over the judged window. Not a limit.");
        md.AppendLine();
        md.AppendLine("| Step | Bots | " + string.Join(" | ", stages) + " |");
        md.AppendLine("|---|---|" + string.Concat(stages.Select(_ => "---|")));
        foreach (StepRecord step in steps)
        {
            PostUpdateStages post = step.Server.PostUpdate;
            string cells = post.Readout switch
            {
                PostUpdateReadout.NotExported => "not exported by this world build |" + string.Concat(stages.Skip(1).Select(_ => " |")),
                PostUpdateReadout.Unknown => string.Join(" | ", stages.Select(_ => "n/a")) + " |",
                _ => string.Join(" | ", stages.Select(stage => post.Stages.FirstOrDefault(t => t.Stage == stage) is { } t
                    ? $"{Number(t.MeanUs, "0")} / {Number(t.P99Us, "0")}"
                    : "n/a")) + " |",
            };
            md.AppendLine(Invariant($"| {step.Index} | {step.Bots} | ") + cells);
        }
    }

    private static string Json(RampResult result, RampOptions options, RunFile run, string cpu)
    {
        var report = new
        {
            Started = result.Started,
            Ended = result.Ended,
            World = run.WorldId,
            ServerVersion = result.ServerVersion,
            result.ServerVersionAtEnd,
            result.ServerPod,
            result.ServerPodAtEnd,
            result.ContainerRestartsAtStart,
            result.ContainerRestartsAtEnd,
            result.ContainerRestarts,
            result.ContainerStartedAtStart,
            result.ContainerStartedAtEnd,
            result.ContainerLastTerminatedAt,
            result.OldPodLastUpAt,
            result.NewestPod,
            result.NewestPodStartedAt,
            result.NewestPodRestarts,
            result.LastJudgedEnd,
            result.StopStarted,
            RunId = run.RunId,
            RunBots = run.Bots.Count,
            Api = run.Api.ToString(),
            options.Mix,
            Ramp = new
            {
                options.Start,
                options.Step,
                HoldSeconds = options.Hold.TotalSeconds,
                JudgedWindowSeconds = RampRunner.JudgedWindow(options.Hold).TotalSeconds,
                options.Max,
                options.SignInConcurrency,
                options.Dial,
                Prometheus = options.Prometheus.ToString(),
                options.Pod,
                ForestTimeSeconds = options.ForestTime.TotalSeconds,
                options.PartySize,
            },
            Limits = options.Limits.Select(limit => new { limit.Name, limit.CliName, limit.Threshold, limit.TripsAbove, limit.Unit }),
            BotPc = new { Cpu = cpu, Cores = Environment.ProcessorCount, ClockOffsetSeconds = result.ClockOffset.TotalSeconds },
            Result = new
            {
                result.Outcome,
                Text = ResultLine(result),
                result.Capacity,
                result.FailedFirst,
                result.StopReason,
            },
            result.Steps,
            Notes = new
            {
                result.ServerVersionChanged,
                result.ServerPodChanged,
                result.ServerChange,
                result.ServerRestarted,
                result.RestartProvenDuringRamp,
                result.RestartedAfterLastJudgedStep,
                result.RestartNotProvenAfter,
                result.RestartNotProvenReason,
                result.KubeNotScrapedSinceDrain,
                result.Stands,
                result.DoesNotStandReason,
                RestartCheck = RestartCheckText(result.RestartCheck),
                result.RestartCheckReason,
                Blips = result.Steps.Where(step => step.Decision.Blip).Select(step => step.Index),
                SlowKicksMayBeGenerator = result.Steps.Where(step => step.Decision.SlowKicksMayBeGenerator).Select(step => step.Index),
                result.SignIns,
                SecondsPerSignIn = result.SignInRate.TotalSeconds,
                result.SignInFailures,
                result.SignOutFailures,
                result.StopLeaveFailures,
                result.WorldDrained,
                result.SignOutsSkipped,
                result.LeavesSkipped,
            },
        };
        return JsonSerializer.Serialize(report, s_json);
    }

    /// <summary>
    /// The header's server version: the change during the ramp when one was seen; otherwise the version (marked when
    /// read only at the end), with the restart check when it could not be made whole.
    /// </summary>
    private static string ServerVersionText(RampResult result)
    {
        if (result.ServerChange is { } change) return change;

        string version = result.ServerVersion ??
            (result.ServerVersionAtEnd is { } atEnd ? $"{atEnd} (read at the end)" : "unknown");
        return result.RestartCheck == RestartCheck.Complete
            ? version
            : $"{version} (restart check: {RestartCheckText(result.RestartCheck)}, {result.RestartCheckReason})";
    }

    private static string RestartCheckText(RestartCheck check) => check switch
    {
        RestartCheck.Complete => "complete",
        RestartCheck.Partial => "partial",
        _ => "unknown",
    };

    /// <summary>The bot PC's CPU model, or <c>unknown</c>.</summary>
    private static string CpuModel()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return (Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0",
                    "ProcessorNameString", null) as string)?.Trim() ?? "unknown";
            }

            if (File.Exists("/proc/cpuinfo"))
            {
                string? line = File.ReadLines("/proc/cpuinfo").FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal));
                if (line is not null && line.IndexOf(':', StringComparison.Ordinal) is var colon and >= 0)
                    return line[(colon + 1)..].Trim();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Unknown, then.
        }

        return "unknown";
    }

    /// <summary>Counts by kind, the most frequent first: <c>join:ACTIVE_GAME_SESSION 3, spawn:timeout 1</c>; empty with none.</summary>
    private static string Kinds(IReadOnlyDictionary<string, int> counts) =>
        string.Join(", ", counts.OrderByDescending(f => f.Value).ThenBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => Invariant($"{f.Key} {f.Value}")));

    /// <summary>
    /// The send threads' busy time: <c>0.42 (31 %)</c>, all threads together in cores and the busiest one's share of a
    /// core; <c>not exported</c> for a world build from before the send threads; <c>n/a</c> when not read.
    /// </summary>
    private static string SendBusy(SendThreads send) => send switch
    {
        { Readout: SendReadout.NotExported } => "not exported",
        { BusyCores: { } cores, BusiestThread: { } busiest } => $"{Number(cores, "0.00")} ({Percent(busiest)})",
        _ => "n/a",
    };

    /// <summary>A byte count: <c>512 B</c> below a KiB, <c>12.5 KiB</c> from there; <c>n/a</c> when not read.</summary>
    private static string Bytes(double? bytes) =>
        bytes is not { } b || !double.IsFinite(b) ? "n/a"
        : b < 1024 ? Invariant($"{b:0} B")
        : Invariant($"{b / 1024:0.#} KiB");

    private static string Number(double? value, string format) =>
        value is { } v && double.IsFinite(v) ? v.ToString(format, CultureInfo.InvariantCulture) : "n/a";

    private static string Ms(double? value, string format) =>
        value is { } v && double.IsFinite(v) ? v.ToString(format, CultureInfo.InvariantCulture) + " ms" : "n/a";

    private static string Percent(double? fraction, string format = "0") =>
        fraction is { } v && double.IsFinite(v) ? (v * 100).ToString(format, CultureInfo.InvariantCulture) + " %" : "n/a";

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
