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
        string trace = await CaptureAsync(WorldTickTrace.Log, () =>
        {
            WorldTickTrace.Log.InstanceTick(Marker, Marker + 1, Marker + 2, Marker + 3, Marker + 4, Marker + 5,
                Marker + 6, Marker + 7, Marker + 8, Marker + 9, Marker + 10, Marker + 11, Marker + 12, Marker + 13,
                Marker + 14, Marker + 15, Marker + 16, Marker + 17, Marker + 18, Marker + 19, Marker + 20, Marker + 21,
                Marker + 22, Marker + 23, Marker + 24, Marker + 25, Marker + 26, Marker + 27, Marker + 28, Marker + 29,
                Marker + 30);
            WorldTickTrace.Log.WorldTick(Marker, Marker + 1, Marker + 2, Marker + 3, Marker + 4);
            WorldTickTrace.Log.InstanceBatch(Marker, Marker + 1, Marker + 2, Marker + 3, Marker + 4);
        });

        Assert.Equal(0, TickDump.Run(trace, _directory, TextWriter.Null));

        AssertEveryFieldInItsColumn("instance.csv", nameof(WorldTickTrace.InstanceTick), TickDump.InstanceColumns);
        AssertEveryFieldInItsColumn("world.csv", nameof(WorldTickTrace.WorldTick), TickDump.WorldColumns);
        AssertEveryFieldInItsColumn("batch.csv", nameof(WorldTickTrace.InstanceBatch), TickDump.BatchColumns);
    }

    /// <summary>
    /// A record that arrives without its fields' names, as <c>WorldTick</c> and <c>InstanceBatch</c> did in a
    /// <c>collect-linux</c> capture (#1007), is read by position into the same columns, at whichever version its
    /// payload's length is.
    /// </summary>
    [Fact]
    public async Task Read_a_record_without_field_names_by_position_into_the_same_columns()
    {
        const long OlderMarker = Marker + 0x100;
        string trace = await CaptureAsync(UnnamedTickTrace.Log, () =>
        {
            UnnamedTickTrace.Log.Write(1, Marker, TickDump.InstanceColumns.Count);
            UnnamedTickTrace.Log.Write(2, Marker, TickDump.WorldColumns.Count);
            UnnamedTickTrace.Log.Write(3, Marker, TickDump.BatchColumns.Count);
            // Version 0 of WorldTick, before sessionPackets.
            UnnamedTickTrace.Log.Write(2, OlderMarker, TickDump.WorldColumns.Count - 1);
        });

        Assert.Equal(0, TickDump.Run(trace, _directory, TextWriter.Null));

        AssertEveryFieldInItsColumn("instance.csv", nameof(WorldTickTrace.InstanceTick), TickDump.InstanceColumns);
        AssertEveryFieldInItsColumn("world.csv", nameof(WorldTickTrace.WorldTick), TickDump.WorldColumns);
        AssertEveryFieldInItsColumn("batch.csv", nameof(WorldTickTrace.InstanceBatch), TickDump.BatchColumns);
        string[] older = MarkedRow("world.csv", OlderMarker);
        Assert.Equal(
            Enumerable.Range(0, TickDump.WorldColumns.Count - 1)
                .Select(i => (OlderMarker + i).ToString(CultureInfo.InvariantCulture)).Append(""),
            older.Skip(TickDump.Leading.Count));
    }

    /// <summary>
    /// Records what <paramref name="write" /> writes through <paramref name="writer" />, through an EventPipe session on
    /// this process.
    /// </summary>
    private async Task<string> CaptureAsync(EventSource writer, Action write)
    {
        string trace = Path.Combine(_directory, "tick.nettrace");
        var client = new DiagnosticsClient(Environment.ProcessId);
        using EventPipeSession session = client.StartEventPipeSession(
            new EventPipeProvider(TickDump.Provider, EventLevel.Informational), requestRundown: false);
        await using FileStream file = File.Create(trace);
        Task copy = session.EventStream.CopyToAsync(file);
        Assert.True(SpinWait.SpinUntil(writer.IsEnabled, TimeSpan.FromSeconds(10)));

        write();

        session.Stop();
        await copy;
        return trace;
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
        Assert.Equal(TickDump.Leading.Concat(fields), File.ReadLines(Path.Combine(_directory, fileName)).First().Split(','));

        string[] row = MarkedRow(fileName, Marker);
        for (int i = 0; i < fields.Length; i++)
            Assert.Equal((Marker + i).ToString(CultureInfo.InvariantCulture), row[TickDump.Leading.Count + i]);
    }

    /// <summary>The one row of the file whose first field is <paramref name="marker" />.</summary>
    private string[] MarkedRow(string fileName, long marker)
    {
        string value = marker.ToString(CultureInfo.InvariantCulture);
        return Assert.Single(
            File.ReadLines(Path.Combine(_directory, fileName)).Skip(1).Select(l => l.Split(',')),
            r => r[TickDump.Leading.Count] == value);
    }

    /// <summary>
    /// The tick provider under its own name with its three events declared without parameters, so the trace carries no
    /// field names for them, and each payload written as the provider writes its own: consecutive 64-bit integers.
    /// Its own Guid, since a second source with the provider's (derived from the name) is never enabled; the trace names
    /// the provider by name alone.
    /// </summary>
    [EventSource(Name = TickDump.Provider, Guid = "6b0f7c55-3a2e-4d7c-9a51-1f0b8e2c4d10")]
    private sealed class UnnamedTickTrace : EventSource
    {
        public static readonly UnnamedTickTrace Log = new();

        private UnnamedTickTrace() { }

        [Event(1, Level = EventLevel.Informational)]
        public void InstanceTick() { }

        [Event(2, Level = EventLevel.Informational)]
        public void WorldTick() { }

        [Event(3, Level = EventLevel.Informational)]
        public void InstanceBatch() { }

        /// <summary>Writes event <paramref name="eventId" /> with <paramref name="count" /> fields: <paramref name="first" />, then each one more.</summary>
        [NonEvent]
        public void Write(int eventId, long first, int count) =>
            WriteEvent(eventId, Enumerable.Range(0, count).Select(i => (EventSourcePrimitive)(first + i)).ToArray());
    }
}
