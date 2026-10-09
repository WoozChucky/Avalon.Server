using Avalon.LoadTest;
using Avalon.LoadTest.Api;
using Avalon.LoadTest.Bots;
using Avalon.LoadTest.Runs;

// The load-test bot client: headless bots that sign in over the REST API, enter a world over TLS as the game client
// does, and walk, idle or change character while the run measures how the world server copes.
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
catch (OperationCanceledException) when (cancel.IsCancellationRequested)
{
    Console.Error.WriteLine("Cancelled.");
    return 1;
}

static int Usage()
{
    Console.Error.WriteLine($"""
        Usage:
          dotnet run --project tools/Avalon.LoadTest -- <command> [options]

        Commands:
          provision --count N [--world W] [--api URL] [--run ABC]
              Creates N bot accounts (1 to {CommandLine.MaxBots}) as an admin, who is asked for a username and
              password, and keeps them in a run file. Above 1,000 bots the API makes several runs, each with its
              own id; the file is named after the first. --world is the only world the bots
              enter (default {CommandLine.DefaultWorld}); --api the API origin (default {CommandLine.DefaultApi});
              --run the first run's id, three letters (default: the API picks one).
          check [--run ABC] [--dial HOST] [--bot N] [--behaviour idle|walker]
              One bot end to end: signs in, enters the run's world (join ticket, TLS, admission, handshake,
              create or select, loaded), sends input at 60 Hz for 10 s through the input driver, leaves and
              signs out, printing each step's duration, the input-ack latency and the driver's lateness.
              --dial is the host to connect to instead of the join reply's (TLS still names the reply's
              server); --bot the bot's index in the run (default 0); --behaviour idle (default) or walker.
          cleanup [--run ABC]
              Deletes the run's accounts as an admin and forgets the run file; refused while bots hold live
              sessions. --run names the run, needed only when several are kept.

        Run files are kept in {RunFile.Directory}.
        """);
    return 2;
}
