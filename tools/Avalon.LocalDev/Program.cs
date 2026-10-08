using Avalon.LocalDev;

// The local development helper (docs/development-setup.md): `setup` prepares a plain `dotnet run` of the API and the
// world server, `login` signs in over the REST API and hands the game client its ticket, and `check` walks the same
// chain the client walks, up to a join ticket, and reports what it found.
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stop.Cancel();
};

try
{
    return args switch
    {
        ["setup", .. var rest] when rest.Length == 0 => await SetupCommand.RunAsync(stop.Token),
        ["login", .. var rest] => await LoginCommand.RunAsync(CommandLine.Parse(rest), check: false, stop.Token),
        ["check", .. var rest] => await LoginCommand.RunAsync(CommandLine.Parse(rest), check: true, stop.Token),
        _ => Usage(),
    };
}
catch (CommandLineException error)
{
    await Console.Error.WriteLineAsync(error.Message);
    return Usage();
}
catch (LocalDevException error)
{
    await Console.Error.WriteLineAsync(error.Message);
    return 1;
}

static int Usage()
{
    Console.Error.WriteLine("""
        Usage:
          dotnet run --project tools/Avalon.LocalDev -- setup
              Makes the world's three local TLS certificates into certificates/local/ and writes the API's and the
              world server's user-secrets for them (and the API's signing keys, if it has none yet).
          dotnet run --project tools/Avalon.LocalDev -- login [options]
              Signs in and writes a game ticket, one line, to standard output (everything else goes to standard error).
          dotnet run --project tools/Avalon.LocalDev -- check [options]
              Signs in, redeems the ticket as the game client would, lists the worlds and asks for a join ticket.

        Options (login and check):
          --user <name>        the account, default ADMIN
          --password <value>   its password; default: AVALON_DEV_PASSWORD, else asked for
          --api <url>          the API's https origin, default https://localhost:7166
          --launch <runtime>   login only: start the game client (runtime.exe) with the ticket on its standard input
          --world <id>         check only: the world to ask a join ticket for, default the first one listed
        """);
    return 2;
}
