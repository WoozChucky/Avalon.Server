using Avalon.LoadTest;
using Avalon.LoadTest.Api;
using Avalon.LoadTest.Bots;
using Avalon.LoadTest.Ramp;
using Avalon.LoadTest.Report;
using Avalon.LoadTest.Runs;

// The load-test bot client: headless bots that sign in over the REST API, enter a world over TLS as the game client
// does, and walk, idle, change character or fight through the forest while the run measures how the world server copes.
using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, press) =>
{
    // The first Ctrl+C cancels what runs; a second one ends the process.
    if (cancel.IsCancellationRequested) return;
    press.Cancel = true;
    cancel.Cancel();
};

try
{
    return args switch
    {
        ["provision", .. var options] => await RunCommands.ProvisionAsync(CommandLine.ParseProvision(options), cancel.Token),
        ["check", .. var options] => await CheckCommand.RunAsync(CommandLine.ParseCheck(options), cancel.Token),
        ["ramp", .. var options] => await RampAsync(CommandLine.ParseRamp(options), cancel.Token),
        ["cleanup", .. var options] => await RunCommands.CleanupAsync(CommandLine.ParseCleanup(options), cancel.Token),
        [] => Usage(),
        [var command, ..] => throw new CommandLineException($"Unknown command {command}."),
    };
}
catch (CommandLineException error)
{
    Console.Error.WriteLine(error.Message);
    return Usage();
}
catch (ApiException error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
catch (PrometheusException error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
catch (OperationCanceledException) when (cancel.IsCancellationRequested)
{
    Console.Error.WriteLine("Cancelled.");
    return 1;
}

// The capacity run, then its report, which is written however the ramp ended: first as soon as the outcome is known
// (before the bots leave, which a second Ctrl+C would cut short), then again over it once the stop sequence is done.
// Exit 0 when the ramp reached a verdict (a capacity, no limit reached, the bot PC saturated), 1 when it stopped short
// of one or the run does not stand (the world server restarted and that it came after the last judged window is not
// proven, or no restart was seen but the check could not be made whole); a report that could not be completed is said,
// and does not change the exit code.
static async Task<int> RampAsync(RampArguments arguments, CancellationToken ct)
{
    var run = RunFile.Load(arguments.RunId, forBots: true);
    RampOptions options = arguments.For(run);
    string dialling = options.Dial is null ? "" : $", dialling {options.Dial}";
    Console.WriteLine(
        $"Ramp of run {run.RunId} into world {run.WorldId}: {options.Start} bots, +{options.Step} a step, up to {options.Max}, " +
        $"each held {options.Hold.TotalSeconds:0} s{dialling}. Ctrl+C stops it and still writes the report.");

    string? report = null;
    string? earlyResult = null;
    RampResult result = await new RampRunner(run, options).RunAsync(known =>
    {
        report = ReportWriter.Save(known, options, run);
        earlyResult = ReportWriter.ResultLine(known);
        Console.WriteLine($"Result: {earlyResult}");
        foreach (Breach breach in known.FailedFirst)
            Console.WriteLine($"  failed first: {RampRunner.Describe(breach, options.Limits)}");
        Console.WriteLine($"Report: {report} (and .json); completed once the bots have left.");
    }, ct);
    // The end reads after the bots left can change whether the run stands.
    string finalResult = ReportWriter.ResultLine(result);
    if (!string.Equals(finalResult, earlyResult, StringComparison.Ordinal))
        Console.WriteLine($"Result: {finalResult}");

    try
    {
        report = ReportWriter.Save(result, options, run, report);
        Console.WriteLine($"Report: {report} (and .json)");
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    {
        // The bots are gone and the verdict stands: only the stop's part of the report is lost.
        Console.Error.WriteLine(report is null
            ? $"Writing the report failed: {error.Message}. No report was written."
            : $"Completing the report failed: {error.Message}. The report saved before the stop remains: {report} (and .json), without the stop's results.");
    }

    return result.Stands &&
        result.Outcome is RampOutcome.Capacity or RampOutcome.NoLimitReached or RampOutcome.GeneratorSaturated
        ? 0
        : 1;
}

static int Usage()
{
    Console.Error.WriteLine($"""
        Usage:
          dotnet run --project tools/Avalon.LoadTest -- <command> [options]

        Commands:
          provision --count N [--world W] [--api URL] [--run ABC]
              Creates N bot accounts (1 to {CommandLine.MaxBots}) as an admin, who is asked for a username and
              password, and keeps them in a run file. Above 1,000 bots it asks the API for several runs, each
              with its own id; the file is named after the first. --world is the only world the bots
              enter (default {CommandLine.DefaultWorld}); --api the API origin (default {CommandLine.DefaultApi});
              --run the first run's id, three letters (default: the tool picks one no kept run file lists).
          check [--run ABC] [--dial HOST] [--bot N] [--behaviour idle|walker|fighter] [--forest-time 5m]
              One bot end to end: signs in, enters the run's world (join ticket, TLS, admission, handshake,
              create or select, loaded), sends input at 60 Hz for 10 s through the input driver, leaves and
              signs out, printing each step's duration, the input-ack latency and the driver's lateness.
              --dial is the host to connect to instead of the join reply's (TLS still names the reply's
              server); --bot the bot's index in the run (default 0); --behaviour idle (default), walker, or
              fighter: one forest trip instead of the 10 s (portal, fight, exit), passed when it walks out
              into town; --forest-time how long it fights (default 5m, as 90s, 5m or seconds).
          ramp [--run ABC] [--mix idle=60,walker=30,churner=10] [--start 50] [--step 50] [--hold 90s] [--max N]
               [--limit name=value]... [--dial HOST] [--prometheus URL] [--pod NAME] [--sign-in-concurrency 8]
               [--forest-time 5m]
              The capacity run: adds bots in steps of --step from --start up to --max (default: the run's size),
              holds each step --hold (a settle of at least 30 s, then judged on up to its last 60 s), and stops
              at the first limit breached twice in a row. Behaviours are shared by --mix (idle, walker, churner,
              fighter); a fighter's trip stays --forest-time in the forest (default 5m). --limit overrides a limit (repeatable):
              {string.Join(", ", Limits.Defaults.Select(l => l.CliName))}; e.g. tick-p99=20, memory=0.9.
              --prometheus (default {CommandLine.DefaultPrometheus}) and --pod (default {CommandLine.DefaultPod})
              locate the server's metrics; --sign-in-concurrency bounds sign-ins at once (default 8). Every bot
              leaves at the end; the report goes to {ReportWriter.Directory}.
          cleanup [--run ABC]
              Deletes the run's accounts as an admin and forgets the run file; refused while bots hold live
              sessions. --run names the run, needed only when several are kept.

        Run files are kept in {RunFile.Directory}.
        """);
    return 2;
}
