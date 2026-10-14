using System.Diagnostics.Tracing;
using System.Globalization;
using System.Reflection;
using Avalon.TraceReader;
using Avalon.World.Telemetry;
using Microsoft.Diagnostics.NETCore.Client;

namespace Avalon.Server.World.UnitTests.Telemetry;

/// <summary>
/// The trace reader's <c>tick</c> mode against a real trace of this process: every field the provider writes must land
/// in the column of its name, whole, and the reader must have a column for every field. A field added to the provider
/// without the reader, or a column renamed, breaks every analysis silently; this breaks here instead.
/// </summary>
[Collection("World tick trace")]
public sealed class TickTraceReaderShould : IDisposable
{
    // Above 2^53, so a value read through a double would lose its low bits and fail.
    private const long Marker = 0x7000_0000_0000_0000;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tick-trace-{Guid.NewGuid():N}");

    public TickTraceReaderShould() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task Write_every_field_of_every_record_whole_to_the_column_of_its_name()
    {
        string trace = Path.Combine(_directory, "tick.nettrace");
        var client = new DiagnosticsClient(Environment.ProcessId);
        using (EventPipeSession session = client.StartEventPipeSession(
                   new EventPipeProvider(TickDump.Provider, EventLevel.Informational), requestRundown: false))
        {
            await using FileStream file = File.Create(trace);
            Task copy = session.EventStream.CopyToAsync(file);
            Assert.True(SpinWait.SpinUntil(WorldTickTrace.Log.IsEnabled, TimeSpan.FromSeconds(10)));

            WorldTickTrace.Log.InstanceTick(Marker, Marker + 1, Marker + 2, Marker + 3, Marker + 4, Marker + 5,
                Marker + 6, Marker + 7, Marker + 8, Marker + 9, Marker + 10, Marker + 11, Marker + 12, Marker + 13,
                Marker + 14, Marker + 15, Marker + 16, Marker + 17, Marker + 18, Marker + 19, Marker + 20, Marker + 21,
                Marker + 22, Marker + 23, Marker + 24, Marker + 25, Marker + 26, Marker + 27, Marker + 28, Marker + 29,
                Marker + 30);
            WorldTickTrace.Log.WorldTick(Marker, Marker + 1, Marker + 2, Marker + 3, Marker + 4);
            WorldTickTrace.Log.InstanceBatch(Marker, Marker + 1, Marker + 2, Marker + 3, Marker + 4);

            session.Stop();
            await copy;
        }

        Assert.Equal(0, TickDump.Run(trace, _directory, TextWriter.Null));

        AssertEveryFieldInItsColumn("instance.csv", nameof(WorldTickTrace.InstanceTick), TickDump.InstanceColumns);
        AssertEveryFieldInItsColumn("world.csv", nameof(WorldTickTrace.WorldTick), TickDump.WorldColumns);
        AssertEveryFieldInItsColumn("batch.csv", nameof(WorldTickTrace.InstanceBatch), TickDump.BatchColumns);
    }

    /// <summary>
    /// The file's header is the leading columns then the event's parameters, in order, and the marked row holds in each
    /// column the value written to the parameter of that position: <see cref="Marker" /> plus its index.
    /// </summary>
    private void AssertEveryFieldInItsColumn(string fileName, string eventName, IReadOnlyList<string> columns)
    {
        string[] fields = typeof(WorldTickTrace).GetMethod(eventName, BindingFlags.Public | BindingFlags.Instance)!
            .GetParameters().Select(p => p.Name!).ToArray();
        Assert.Equal(fields, columns);

        string[] lines = File.ReadAllLines(Path.Combine(_directory, fileName));
        Assert.Equal(TickDump.Leading.Concat(fields), lines[0].Split(','));

        string marker = Marker.ToString(CultureInfo.InvariantCulture);
        string[] row = Assert.Single(lines.Skip(1).Select(l => l.Split(',')), r => r[TickDump.Leading.Count] == marker);
        for (int i = 0; i < fields.Length; i++)
            Assert.Equal((Marker + i).ToString(CultureInfo.InvariantCulture), row[TickDump.Leading.Count + i]);
    }
}
