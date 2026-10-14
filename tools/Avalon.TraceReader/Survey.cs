using System.Globalization;
using Microsoft.Diagnostics.Tracing;

namespace Avalon.TraceReader;

/// <summary>
/// The <c>survey</c> mode: what a trace holds, before reading it. Every event type (provider, name, id and version) with
/// its count, the processes the events came from, and the session's start, length, pointer size and processor count.
/// Run it first on a capture of a new kind, to check it holds what it was taken for.
/// </summary>
public static class Survey
{
    public static int Run(string trace, TextWriter log)
    {
        using var source = new EventPipeEventSource(trace);
        var types = new Dictionary<string, long>(StringComparer.Ordinal);
        var processes = new Dictionary<int, long>();
        void Count(TraceEvent e)
        {
            string type = string.Create(CultureInfo.InvariantCulture,
                $"{e.ProviderName}/{e.EventName} (id {(int)e.ID}, version {e.Version})");
            types[type] = types.GetValueOrDefault(type) + 1;
            processes[e.ProcessID] = processes.GetValueOrDefault(e.ProcessID) + 1;
        }

        // Each event goes to the first parser that knows it: the runtime's, then any described by its own metadata (an
        // EventSource such as Avalon-World-Tick), then, unnamed, the rest.
        source.Clr.All += Count;
        source.Dynamic.All += Count;
        source.UnhandledEvents += Count;
        source.Process();

        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"session start {source.SessionStartTime.ToUniversalTime():O}, duration {source.SessionDuration}, pointer size {source.PointerSize}, processors {source.NumberOfProcessors}"));
        foreach ((string type, long count) in types.OrderByDescending(t => t.Value).ThenBy(t => t.Key, StringComparer.Ordinal))
            log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{count,10}  {type}"));
        foreach ((int process, long count) in processes.OrderByDescending(p => p.Value))
            log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"process {process}: {count} events"));
        return 0;
    }
}
