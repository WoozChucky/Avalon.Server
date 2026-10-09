using Avalon.LoadTest;

// The load-test bot client: headless bots that sign in over the REST API, enter a world over TLS as the game client
// does, and walk, idle or change character while the run measures how the world server copes.
try
{
    return args switch
    {
        [] => Usage(),
        [var command, ..] => throw new CommandLineException($"Unknown command {command}."),
    };
}
catch (CommandLineException error)
{
    Console.Error.WriteLine(error.Message);
    return Usage();
}

static int Usage()
{
    Console.Error.WriteLine("""
        Usage:
          dotnet run --project tools/Avalon.LoadTest -- <command> [options]

        No command is available yet.
        """);
    return 2;
}
