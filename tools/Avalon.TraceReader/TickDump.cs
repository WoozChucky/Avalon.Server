using System.Globalization;
using Microsoft.Diagnostics.Tracing;

namespace Avalon.TraceReader;

/// <summary>
/// The <c>tick</c> mode: every record of the <c>Avalon-World-Tick</c> provider to one CSV per event kind, a row per
/// event. Columns are fixed, the provider's fields in its order (docs/tick-profiling.md) after the thread and the time,
/// and each is filled from the field of that name, so a record of an older version, without the fields appended since,
/// leaves those cells empty. Values are written as 64-bit integers.
/// </summary>
public static class TickDump
{
    public const string Provider = "Avalon-World-Tick";

    /// <summary>The columns every file starts with: the writing thread and the event's time in ms from the session start.</summary>
    public static IReadOnlyList<string> Leading { get; } = ["thread", "ts_ms"];

    /// <summary>Event 1, <c>InstanceTick</c>, version 1.</summary>
    public static IReadOnlyList<string> InstanceColumns { get; } =
    [
        "tickId", "instanceLow", "instanceHigh", "mapType", "startTimestamp", "durationTicks", "frequency", "completed",
        "broadcast", "players", "creatures", "projectiles", "inputTicks", "combatTicks", "creatureTicks", "dirtyTicks",
        "visibilityTicks", "trackingTicks", "statePreparationTicks", "stateEncodingTicks", "auraTicks", "enqueueTicks",
        "candidateChecks", "visiblePairs", "dirtyEntities", "added", "updated", "removed", "statePackets", "stateBytes",
        "mapPackets",
    ];

    /// <summary>Event 2, <c>WorldTick</c>, version 1.</summary>
    public static IReadOnlyList<string> WorldColumns { get; } =
        ["tickId", "durationTicks", "frequency", "completed", "sessionPackets"];

    /// <summary>Event 3, <c>InstanceBatch</c>, version 0.</summary>
    public static IReadOnlyList<string> BatchColumns { get; } =
        ["tickId", "durationTicks", "frequency", "instances", "completed"];

    /// <summary>Writes instance.csv, world.csv and batch.csv to <paramref name="outDir" />; returns 0.</summary>
    public static int Run(string trace, string outDir, TextWriter log)
    {
        using var source = new EventPipeEventSource(trace);
        using var instances = new File(Path.Combine(outDir, "instance.csv"), InstanceColumns);
        using var worlds = new File(Path.Combine(outDir, "world.csv"), WorldColumns);
        using var batches = new File(Path.Combine(outDir, "batch.csv"), BatchColumns);
        var unknown = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var unknownFields = new SortedSet<string>(StringComparer.Ordinal);

        source.Dynamic.All += e =>
        {
            if (e.ProviderName != Provider)
                return;

            File? file = (int)e.ID switch
            {
                1 => instances,
                2 => worlds,
                3 => batches,
                _ => null,
            };
            if (file is null)
            {
                string name = $"event {(int)e.ID} {e.EventName}";
                unknown[name] = unknown.GetValueOrDefault(name) + 1;
                return;
            }

            file.Write(e, unknownFields);
        };
        source.Process();

        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"session start {source.SessionStartTime.ToUniversalTime():O}, duration {source.SessionDuration}"));
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"instance.csv {instances.Rows} rows, world.csv {worlds.Rows} rows, batch.csv {batches.Rows} rows"));
        foreach ((string name, int count) in unknown)
            log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"not written, no file for it: {name} x{count}"));
        foreach (string field in unknownFields)
            log.WriteLine($"not written, no column for it: {field}");
        return 0;
    }

    /// <summary>One output file: its fixed header, then a row per event, each column from the field of its name.</summary>
    private sealed class File : IDisposable
    {
        private readonly StreamWriter _writer;
        private readonly IReadOnlyList<string> _columns;

        // Per payload layout (TraceEvent shares one names array per event template): each column's field index, or -1.
        private readonly Dictionary<string[], int[]> _layouts = new(ReferenceEqualityComparer.Instance);

        public File(string path, IReadOnlyList<string> columns)
        {
            _columns = columns;
            _writer = new StreamWriter(path) { NewLine = "\n" };
            _writer.WriteLine(string.Join(",", Leading.Concat(columns)));
        }

        public long Rows { get; private set; }

        public void Write(TraceEvent e, ISet<string> unknownFields)
        {
            string[] names = e.PayloadNames;
            if (!_layouts.TryGetValue(names, out int[]? layout))
            {
                layout = new int[_columns.Count];
                for (int c = 0; c < layout.Length; c++)
                    layout[c] = Array.IndexOf(names, _columns[c]);
                foreach (string name in names)
                {
                    if (!_columns.Contains(name))
                        unknownFields.Add($"{e.EventName}.{name}");
                }

                _layouts.Add(names, layout);
            }

            _writer.Write(e.ThreadID.ToString(CultureInfo.InvariantCulture));
            _writer.Write(',');
            _writer.Write(e.TimeStampRelativeMSec.ToString("F4", CultureInfo.InvariantCulture));
            foreach (int field in layout)
            {
                _writer.Write(',');
                if (field >= 0)
                    _writer.Write(Convert.ToInt64(e.PayloadValue(field), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
            }

            _writer.WriteLine();
            Rows++;
        }

        public void Dispose() => _writer.Dispose();
    }
}
