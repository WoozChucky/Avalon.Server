using System.Globalization;
using System.Text.Json;
using Avalon.World.Testing.Scenarios;

// The scenario runner (docs/tooling.md): runs the world server's fixed scenarios in process and reports each one's
// tick-thread allocations, tick time and garbage collections; optionally writes the reports, compares them with an
// earlier run, or writes the committed allocation baseline.
CommandLine options;
try
{
    options = CommandLine.Parse(args);
}
catch (CommandLineException error)
{
    Console.Error.WriteLine(error.Message);
    return Usage();
}

#if DEBUG
Console.Error.WriteLine("A Debug build: its timings and allocations are not the server's; run with -c Release.");
#endif

var reports = new List<ScenarioReport>(options.Scenarios.Count);
foreach (IScenario scenario in options.Scenarios)
{
    Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{scenario.Name}: {options.WarmupSeconds} s warm-up, {ScenarioMeasurement.Windows} windows of {ScenarioMeasurement.WindowTicks} ticks, {options.MeasureTicks} timed ticks"));
    reports.Add(ScenarioMeasurement.Run(scenario, TimeSpan.FromSeconds(options.WarmupSeconds), options.MeasureTicks));
}

PrintReports(reports);

try
{
    if (options.Json is not null)
        WriteReports(options.Json, reports);
    if (options.WriteBaseline is not null)
        WriteReports(options.WriteBaseline, reports);
    if (options.WriteAllocations is not null)
        WriteAllocations(options.WriteAllocations, reports);
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

// A comparison informs; it never fails the run.
if (options.Baseline is not null)
    CompareWithBaseline(options.Baseline, reports);

return 0;

static void PrintReports(IReadOnlyList<ScenarioReport> reports)
{
    RuntimeStamp runtime = reports[0].Runtime;
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{runtime.Runtime}, server GC {(runtime.ServerGc ? "on" : "off")}, latency {runtime.LatencyMode}, {runtime.ProcessorCount} processors, commit {runtime.Commit ?? "unknown"}"));
    Console.WriteLine();

    var rows = new List<string[]>
    {
        new[]
        {
            "scenario", "players", "instances", "bytes/window", "B/player/tick", "tick ms mean", "p95", "p99", "max",
            "% > 16.7 ms", "gen0", "gen1", "gen2", "GC pause ms", "GC pause %",
        },
    };
    foreach (ScenarioReport r in reports)
    {
        rows.Add(
        [
            r.Scenario, Count(r.Players), Count(r.Instances), Count(r.BytesPerWindow), Fixed(r.BytesPerPlayerPerTick, 2),
            Fixed(r.TickMsMean, 3), Fixed(r.TickMsP95, 3), Fixed(r.TickMsP99, 3), Fixed(r.TickMsMax, 3),
            Fixed(r.TicksOverBudgetPercent, 2), Count(r.Gen0), Count(r.Gen1), Count(r.Gen2),
            Fixed(r.GcPauseMs, 2), Fixed(r.GcPausePercent, 2),
        ]);
    }

    PrintTable(rows);
}

static void WriteReports(string path, IReadOnlyList<ScenarioReport> reports)
{
    string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
    if (!string.IsNullOrEmpty(directory))
        Directory.CreateDirectory(directory);

    using FileStream stream = File.Create(path);
    JsonSerializer.Serialize(stream, reports, ScenarioReport.JsonOptions);
    stream.WriteByte((byte)'\n');
    Console.Error.WriteLine($"Wrote {path}");
}

static void WriteAllocations(string path, IReadOnlyList<ScenarioReport> reports)
{
    var scenarios = new Dictionary<string, AllocationBaseline.Entry>(StringComparer.Ordinal);
    foreach (ScenarioReport report in reports)
        scenarios[report.Scenario] = new AllocationBaseline.Entry(report.BytesPerWindow, report.BytesPerPlayerPerTick);

    string date = TimeProvider.System.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    AllocationBaseline.Write(path, new AllocationBaseline.File(reports[0].Runtime.Commit, date, scenarios));
    Console.Error.WriteLine($"Wrote {path}");
}

static void CompareWithBaseline(string path, IReadOnlyList<ScenarioReport> reports)
{
    ScenarioReport[] baseline;
    try
    {
        using FileStream stream = File.OpenRead(path);
        baseline = JsonSerializer.Deserialize<ScenarioReport[]>(stream, ScenarioReport.JsonOptions) ?? [];
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
    {
        Console.Error.WriteLine($"No comparison: {path} could not be read as a scenario report ({error.Message})");
        return;
    }

    foreach (ScenarioReport current in reports)
    {
        Console.WriteLine();
        ScenarioReport? before = Array.Find(baseline, b => string.Equals(b.Scenario, current.Scenario, StringComparison.Ordinal));
        if (before is null)
        {
            Console.WriteLine($"{current.Scenario}: not in {path}");
            continue;
        }

        Console.WriteLine($"{current.Scenario} against {path} (commit {before.Runtime.Commit ?? "unknown"})");
        if (before.Runtime.ServerGc != current.Runtime.ServerGc
            || before.Runtime.ProcessorCount != current.Runtime.ProcessorCount
            || !string.Equals(before.Runtime.Runtime, current.Runtime.Runtime, StringComparison.Ordinal))
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  measured on another setup: {before.Runtime.Runtime}, server GC {before.Runtime.ServerGc}, {before.Runtime.ProcessorCount} processors"));
        }

        var rows = new List<string[]> { new[] { "metric", "current", "baseline", "change" } };
        foreach (Metric metric in Metric.All)
        {
            double now = metric.Read(current), then = metric.Read(before);
            rows.Add([metric.Name, Fixed(now, metric.Decimals), Fixed(then, metric.Decimals), Change(now, then)]);
        }

        PrintTable(rows);
    }
}

static string Change(double current, double baseline)
{
    if (baseline == 0)
        return current == 0 ? "0.0%" : "new";
    return ((current - baseline) * 100d / baseline).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%";
}

// The first column left-aligned, every other right-aligned, each as wide as its widest cell.
static void PrintTable(List<string[]> rows)
{
    int[] widths = new int[rows[0].Length];
    foreach (string[] row in rows)
    {
        for (int c = 0; c < row.Length; c++)
            widths[c] = Math.Max(widths[c], row[c].Length);
    }

    foreach (string[] row in rows)
    {
        var line = new System.Text.StringBuilder();
        for (int c = 0; c < row.Length; c++)
        {
            if (c > 0)
                line.Append("  ");
            line.Append(c == 0 ? row[c].PadRight(widths[c]) : row[c].PadLeft(widths[c]));
        }

        Console.WriteLine(line.ToString().TrimEnd());
    }
}

static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

static string Fixed(double value, int decimals) => value.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

static int Usage()
{
    Console.Error.WriteLine($"""
        Usage:
          dotnet run -c Release --project tools/Avalon.Scenarios -- [options]
              Runs the world server's scenarios in process and prints, per scenario, the tick-thread bytes of one
              {ScenarioMeasurement.WindowTicks}-tick window (the least of {ScenarioMeasurement.Windows}), tick times, ticks over the 60 Hz budget and garbage collections.

        Options:
          --scenario <name>|all       the scenario to run, default all ({string.Join(", ", Scenarios.All.Select(s => s.Name))})
          --warmup-seconds <s>        wall-clock warm-up before measuring, default {CommandLine.DefaultWarmupSeconds}
          --measure-ticks <n>         ticks timed after the allocation windows, default {CommandLine.DefaultMeasureTicks}; 0 skips timing
          --json <file>               writes the reports, a JSON array
          --write-baseline <file>     the same, meant as a baseline for --baseline (perf/local/ is ignored by git)
          --baseline <file>           compares with an earlier --json or --write-baseline file: current, baseline, change
          --write-allocations <file>  writes the allocation baseline (perf/scenario-allocations.json) from this run
        """);
    return 2;
}

/// <summary>The runner's options.</summary>
internal sealed record CommandLine(
    IReadOnlyList<IScenario> Scenarios, int WarmupSeconds, int MeasureTicks,
    string? Json, string? Baseline, string? WriteBaseline, string? WriteAllocations)
{
    public const int DefaultWarmupSeconds = 10;
    public const int DefaultMeasureTicks = 3600;

    public static CommandLine Parse(string[] args)
    {
        string scenario = "all";
        int warmupSeconds = DefaultWarmupSeconds, measureTicks = DefaultMeasureTicks;
        string? json = null, baseline = null, writeBaseline = null, writeAllocations = null;
        for (int i = 0; i < args.Length; i++)
        {
            string option = args[i];
            if (i + 1 == args.Length) throw new CommandLineException($"{option} needs a value.");
            string value = args[++i];
            switch (option)
            {
                case "--scenario": scenario = value; break;
                case "--warmup-seconds": warmupSeconds = Count(option, value); break;
                case "--measure-ticks": measureTicks = Count(option, value); break;
                case "--json": json = value; break;
                case "--baseline": baseline = value; break;
                case "--write-baseline": writeBaseline = value; break;
                case "--write-allocations": writeAllocations = value; break;
                default: throw new CommandLineException($"Unknown option {option}.");
            }
        }

        return new(Pick(scenario), warmupSeconds, measureTicks, json, baseline, writeBaseline, writeAllocations);
    }

    private static IReadOnlyList<IScenario> Pick(string name)
    {
        if (string.Equals(name, "all", StringComparison.Ordinal))
            return Avalon.World.Testing.Scenarios.Scenarios.All;

        foreach (IScenario scenario in Avalon.World.Testing.Scenarios.Scenarios.All)
        {
            if (string.Equals(scenario.Name, name, StringComparison.Ordinal))
                return [scenario];
        }

        throw new CommandLineException($"Unknown scenario {name}.");
    }

    private static int Count(string option, string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int count)
            ? count
            : throw new CommandLineException($"{option} takes a whole number, 0 or more.");
}

/// <summary>A command line the runner cannot run; the usage follows.</summary>
internal sealed class CommandLineException(string message) : Exception(message);

/// <summary>One figure of a report, as the baseline comparison prints it.</summary>
internal sealed record Metric(string Name, Func<ScenarioReport, double> Read, int Decimals)
{
    public static IReadOnlyList<Metric> All { get; } =
    [
        new("bytes/window", r => r.BytesPerWindow, 0),
        new("B/player/tick", r => r.BytesPerPlayerPerTick, 2),
        new("bytes/tick mean", r => r.BytesPerTickMean, 1),
        new("bytes/tick p95", r => r.BytesPerTickP95, 0),
        new("bytes/tick max", r => r.BytesPerTickMax, 0),
        new("total allocated", r => r.TotalAllocatedBytes, 0),
        new("tick ms mean", r => r.TickMsMean, 3),
        new("tick ms p50", r => r.TickMsP50, 3),
        new("tick ms p95", r => r.TickMsP95, 3),
        new("tick ms p99", r => r.TickMsP99, 3),
        new("tick ms max", r => r.TickMsMax, 3),
        new("% > 16.7 ms", r => r.TicksOverBudgetPercent, 2),
        new("gen0", r => r.Gen0, 0),
        new("gen1", r => r.Gen1, 0),
        new("gen2", r => r.Gen2, 0),
        new("GC pause ms", r => r.GcPauseMs, 2),
        new("GC pause %", r => r.GcPausePercent, 2),
    ];
}
