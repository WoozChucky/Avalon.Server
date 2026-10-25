using Avalon.Crashes.Worker;
using Avalon.Crashes.Worker.Maintenance;

try
{
    if (args is [] or ["run"] or ["run", "--paused"])
    {
        await using WebApplication app = CrashWorkerRegistration.BuildRun(args.Contains("--paused"));
        await app.RunAsync();
        return 0;
    }
    if (args[0] is not ("cleanup" or "retry" or "list-failures"))
    {
        Console.Error.WriteLine("Invalid command.");
        return 2;
    }
    HostApplicationBuilder builder = Host.CreateApplicationBuilder([]);
    builder.Logging.ClearProviders();
    builder.Services.AddCrashDatabase(builder.Configuration);
    using IHost host = builder.Build();
    return await host.Services.GetRequiredService<CrashOperatorCommands>().ExecuteAsync(args, Console.Out, CancellationToken.None);
}
catch
{
    // Configuration, connection, SDK and native exceptions may contain private values.
    Console.Error.WriteLine("Crash worker operation failed.");
    return 1;
}
