using Avalon.TraceReader;

// Reads the world server's traces (docs/tick-profiling.md): the Avalon-World-Tick provider's records to CSV, a
// collect-linux trace's kernel CPU samples to per-thread stacks and per-sample rows, or a survey of any trace's events.
try
{
    return args switch
    {
        ["tick", var trace, var outDir] => TickDump.Run(Existing(trace), OutputDirectory(outDir), Console.Out),
        ["kcpu", var trace, var outDir] => KernelSamples.Run(Existing(trace), OutputDirectory(outDir), Console.Out),
        ["survey", var trace] => Survey.Run(Existing(trace), Console.Out),
        // A bad command line: the usage, exit 2.
        _ => Usage(),
    };
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

static string Existing(string trace) =>
    File.Exists(trace) ? trace : throw new FileNotFoundException($"No trace at {trace}.", trace);

static string OutputDirectory(string outDir)
{
    Directory.CreateDirectory(outDir);
    return outDir;
}

static int Usage()
{
    Console.Error.WriteLine("""
        Usage:
          dotnet run -c Release --project tools/Avalon.TraceReader -- tick <trace.nettrace> <out-dir>
              The Avalon-World-Tick records to world.csv, batch.csv and instance.csv (one row per event).
          dotnet run -c Release --project tools/Avalon.TraceReader -- kcpu <trace.nettrace> <out-dir>
              A collect-linux trace's events with stacks, through TraceLog: kstacks.tsv (stacks counted per thread)
              and ksamples.tsv (one row per CPU sample); the .etlx it builds stays in <out-dir>.
          dotnet run -c Release --project tools/Avalon.TraceReader -- survey <trace.nettrace>
              Every event type in the trace with its count, and the processes it holds.

        The columns are described in docs/tick-profiling.md.
        """);
    return 2;
}
