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
/// one row per step, and notes (blips, drops that may be the bots' own, identity's sign-in rate). Kept in
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

    /// <summary><c>%LOCALAPPDATA%\Avalon.LoadTest\reports</c> (on Unix, <c>~/.local/share/Avalon.LoadTest/reports</c>).</summary>
    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Avalon.LoadTest", "reports");

    /// <summary>The report's Markdown and JSON.</summary>
    public static (string Markdown, string Json) Write(RampResult result, RampOptions options, RunFile run)
    {
        string cpu = CpuModel();
        return (Markdown(result, options, run, cpu), Json(result, options, run, cpu));
    }

    /// <summary>Writes the report to <c>&lt;yyyyMMdd-HHmm&gt;-&lt;runId&gt;.md</c> and <c>.json</c> in <see cref="Directory"/>; returns the Markdown's path.</summary>
    public static string Save(RampResult result, RampOptions options, RunFile run)
    {
        (string markdown, string json) = Write(result, options, run);
        System.IO.Directory.CreateDirectory(Directory);
        string stem = Path.Combine(Directory,
            $"{result.Started.ToLocalTime().ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}-{run.RunId}");
        File.WriteAllText(stem + ".md", markdown);
        File.WriteAllText(stem + ".json", json);
        return stem + ".md";
    }

    /// <summary>The result in a line: <c>capacity 300 bots</c>, <c>bot PC saturated: capacity ≥ 450 bots</c>, ...</summary>
    public static string ResultLine(RampResult result) => result.Outcome switch
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
        md.AppendLine($"- World {run.WorldId} server version: {result.ServerVersion ?? "unknown"}");
        md.AppendLine(Invariant($"- Run: {run.RunId} ({run.Bots.Count} bots) through {run.Api}"));
        md.AppendLine($"- Mix: {options.Mix}");
        md.AppendLine(Invariant(
            $"- Ramp: start {options.Start}, step {options.Step}, hold {options.Hold.TotalSeconds:0} s (judged on the last {RampRunner.JudgedWindow(options.Hold).TotalSeconds:0} s), max {options.Max}, {options.SignInConcurrency} sign-ins at once"));
        md.AppendLine($"- Prometheus: {options.Prometheus} (pod {options.Pod}){(options.Dial is { } dial ? $"; dialling {dial}" : "")}");
        md.AppendLine(Invariant($"- Bot PC: {cpu}, {Environment.ProcessorCount} logical cores"));
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
        md.AppendLine("| Step | Bots (idle / walker / churner) | Instances | Tick p99 | TPS | Ack p50 / p95 / p99 | Drops | Receive backlog | Working set | Gen2 / min | GC pause | Save p95 | Entries / failed | Failures by kind | Disconnects | Bot PC CPU | Driver lateness p95 | Verdict |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (StepRecord step in result.Steps)
        {
            ServerValues s = step.Server;
            StepClientValues c = step.Client;
            int failed = c.EntryFailures.Values.Sum();
            string kinds = c.EntryFailures.Count == 0
                ? ""
                : string.Join(", ", c.EntryFailures.OrderByDescending(f => f.Value).Select(f => Invariant($"{f.Key} {f.Value}")));
            string bots = Invariant(
                $"{step.Bots} ({step.ByBehaviour.GetValueOrDefault(BehaviourKind.Idle)} / {step.ByBehaviour.GetValueOrDefault(BehaviourKind.Walker)} / {step.ByBehaviour.GetValueOrDefault(BehaviourKind.Churner)})");
            string workingSet = s.WorkingSetMb is { } mb
                ? Invariant($"{mb:0} MB ({Percent(s.WorkingSetFraction)})")
                : "n/a";
            md.AppendLine(string.Join(" | ",
                "| " + step.Index.ToString(CultureInfo.InvariantCulture),
                bots,
                Number(s.Instances, "0"),
                Ms(s.TickP99Ms, "0.0"),
                Number(s.Tps, "0.0"),
                double.IsFinite(c.AckP95) ? $"{Number(c.AckP50, "0")} / {Number(c.AckP95, "0")} / {Number(c.AckP99, "0")} ms" : "n/a",
                Number(s.Drops, "0"),
                Number(s.ReceiveBacklogMax, "0"),
                workingSet,
                Number(s.Gen2PerMin, "0.##"),
                Percent(s.GcPauseFraction, "0.##"),
                Ms(s.SaveP95Ms, "0"),
                Invariant($"{c.EntryAttempts} / {failed}"),
                kinds,
                c.Disconnects.ToString(CultureInfo.InvariantCulture),
                Percent(step.GeneratorCpu),
                Ms(step.GeneratorLagP95Ms, "0.0"),
                RampRunner.Verdict(step, options.Limits) + " |"));
        }

        md.AppendLine();
        md.AppendLine("## Notes");
        md.AppendLine();
        StepRecord[] blips = [.. result.Steps.Where(step => step.Decision.Blip)];
        md.AppendLine(blips.Length == 0
            ? "- Blips: none."
            : $"- Blips (a breach that passed its re-hold): steps {string.Join(", ", blips.Select(step => step.Index.ToString(CultureInfo.InvariantCulture)))}.");
        StepRecord[] slowReaders = [.. result.Steps.Where(step => step.Decision.DropsMayBeGenerator)];
        if (slowReaders.Length > 0)
        {
            md.AppendLine($"- Drops while the bot PC was above 60 % CPU, possibly the bots reading slowly rather than the server: steps {string.Join(", ", slowReaders.Select(step => step.Index.ToString(CultureInfo.InvariantCulture)))}.");
        }

        md.AppendLine(result.SignIns == 0
            ? Invariant($"- Sign-ins (identity, apart from the world): none; {result.SignInFailures} failures.")
            : Invariant(
                $"- Sign-ins (identity, apart from the world): {result.SignIns} bots, one every {result.SignInRate.TotalSeconds:0.00} s with {options.SignInConcurrency} at once ({60 / result.SignInRate.TotalSeconds:0} per minute); {result.SignInFailures} sign-in or refresh failures."));
        md.AppendLine(result.WorldDrained
            ? "- After the stop the world's players online came back to the count before the ramp."
            : "- After the stop the world's players online had not come back to the count before the ramp within 90 s: wait before cleanup.");
        return md.ToString();
    }

    private static string Json(RampResult result, RampOptions options, RunFile run, string cpu)
    {
        var report = new
        {
            Started = result.Started,
            Ended = result.Ended,
            World = run.WorldId,
            ServerVersion = result.ServerVersion,
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
            },
            Limits = options.Limits.Select(limit => new { limit.Name, limit.CliName, limit.Threshold, limit.TripsAbove, limit.Unit }),
            BotPc = new { Cpu = cpu, Cores = Environment.ProcessorCount },
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
                Blips = result.Steps.Where(step => step.Decision.Blip).Select(step => step.Index),
                DropsMayBeGenerator = result.Steps.Where(step => step.Decision.DropsMayBeGenerator).Select(step => step.Index),
                result.SignIns,
                SecondsPerSignIn = result.SignInRate.TotalSeconds,
                result.SignInFailures,
                result.WorldDrained,
            },
        };
        return JsonSerializer.Serialize(report, s_json);
    }

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

    private static string Number(double? value, string format) =>
        value is { } v && double.IsFinite(v) ? v.ToString(format, CultureInfo.InvariantCulture) : "n/a";

    private static string Ms(double? value, string format) =>
        value is { } v && double.IsFinite(v) ? v.ToString(format, CultureInfo.InvariantCulture) + " ms" : "n/a";

    private static string Percent(double? fraction, string format = "0") =>
        fraction is { } v && double.IsFinite(v) ? (v * 100).ToString(format, CultureInfo.InvariantCulture) + " %" : "n/a";

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
