using System.Globalization;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace Avalon.TraceReader;

/// <summary>
/// The <c>kcpu</c> mode: a <c>dotnet-trace collect-linux</c> trace (kernel CPU samples, context switches, the runtime's
/// and the tick provider's events) read through <see cref="TraceLog" />, which resolves its stacks where
/// <c>dotnet-trace convert</c> finds none. Managed frames resolve from the trace's own method events and kernel frames as
/// <c>vmlinux!...</c>; frames of an unsymbolised module stay raw addresses.
/// </summary>
public static class KernelSamples
{
    /// <summary>
    /// Builds <c>trace.etlx</c> in <paramref name="outDir" />, then writes kstacks.tsv (every event with a stack,
    /// counted by process, thread, event and stack) and ksamples.tsv (one row per CPU sample); returns 0.
    /// </summary>
    public static int Run(string trace, string outDir, TextWriter log)
    {
        string etlx = Path.Combine(outDir, "trace.etlx");
        if (File.Exists(etlx))
            File.Delete(etlx);

        using var traceLog = new TraceLog(TraceLog.CreateFromEventPipeDataFile(trace, etlx));
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"events {traceLog.EventCount}, processes {traceLog.Processes.Count}, threads {traceLog.Threads.Count}"));
        foreach (TraceProcess process in traceLog.Processes)
        {
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"process {process.ProcessID} {process.Name} threads {process.Threads.Count()}"));
        }

        var stacks = new Dictionary<(int Process, int Thread, string Event, string Stack), int>();
        var processors = new SortedDictionary<int, int>();
        long samples = 0;
        using (var rows = new StreamWriter(Path.Combine(outDir, "ksamples.tsv")) { NewLine = "\n" })
        {
            rows.WriteLine("pid\ttid\tcpu\tts_ms");
            foreach (TraceEvent e in traceLog.Events)
            {
                if (e.EventName == "cpu")
                {
                    rows.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"{e.ProcessID}\t{e.ThreadID}\t{e.ProcessorNumber}\t{e.TimeStampRelativeMSec:F4}"));
                    processors[e.ProcessorNumber] = processors.GetValueOrDefault(e.ProcessorNumber) + 1;
                    samples++;
                }

                TraceCallStack? stack = e.CallStack();
                if (stack is null)
                    continue;

                (int, int, string, string) key = (e.ProcessID, e.ThreadID, $"{e.ProviderName}/{e.EventName}", Frames(stack));
                stacks[key] = stacks.GetValueOrDefault(key) + 1;
            }
        }

        using (var output = new StreamWriter(Path.Combine(outDir, "kstacks.tsv")) { NewLine = "\n" })
        {
            output.WriteLine("pid\ttid\tevent\tcount\tstack");
            foreach (((int process, int thread, string name, string frames), int count) in stacks.OrderByDescending(s => s.Value))
                output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{process}\t{thread}\t{name}\t{count}\t{frames}"));
        }

        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"ksamples.tsv {samples} rows, kstacks.tsv {stacks.Count} rows"));
        log.WriteLine("samples by processor: " + string.Join(" ",
            processors.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Key}:{p.Value}"))));
        return 0;
    }

    /// <summary>The stack root first, frames joined by <c>;</c>, each <c>module!method</c> or <c>module!0xaddress</c>.</summary>
    private static string Frames(TraceCallStack leaf)
    {
        var frames = new List<string>();
        for (TraceCallStack? frame = leaf; frame is not null; frame = frame.Caller)
        {
            TraceCodeAddress address = frame.CodeAddress;
            string method = address.FullMethodName;
            if (string.IsNullOrEmpty(method))
                method = string.Create(CultureInfo.InvariantCulture, $"0x{address.Address:x}");
            frames.Add($"{address.ModuleName ?? "?"}!{method}".Replace(';', ',').Replace('\t', ' '));
        }

        frames.Reverse();
        return string.Join(";", frames);
    }
}
