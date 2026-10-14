using System.Diagnostics;
using System.Diagnostics.Tracing;

namespace Avalon.World.Telemetry;

/// <summary>Opt-in EventPipe records for correlating expensive world ticks with instance work; off without a listener.</summary>
[EventSource(Name = "Avalon-World-Tick")]
public sealed class WorldTickTrace : EventSource
{
    public static readonly WorldTickTrace Log = new();

    // One simulation owner per thread. A standalone InstanceTicker supplies its own correlation id.
#pragma warning disable IDE1006 // Thread-local fields use the t_ prefix.
    [ThreadStatic] private static long t_tickId;

    // Running totals of the packets each pass took off the receive queues on this thread, while a listener is attached;
    // a record reads the difference across its own span. Per thread, so instances ticked on several threads (#639)
    // each count their own.
    [ThreadStatic] private static long t_sessionPackets;
    [ThreadStatic] private static long t_mapPackets;
#pragma warning restore IDE1006

    internal static long TickId { get => t_tickId; set => t_tickId = value; }

    /// <summary>Packets the session pass has processed on this thread since the trace was first enabled on it.</summary>
    internal static long SessionPackets => t_sessionPackets;

    /// <summary>Packets the map pass has processed on this thread since the trace was first enabled on it.</summary>
    internal static long MapPackets => t_mapPackets;

    /// <summary>One connection's session pass took <paramref name="count" /> packets off its queue. Nothing while disabled.</summary>
    internal static void CountSessionPackets(uint count)
    {
        if (count != 0 && Log.IsEnabled())
            t_sessionPackets += count;
    }

    /// <summary>One connection's map pass took <paramref name="count" /> packets off its queue. Nothing while disabled.</summary>
    internal static void CountMapPackets(uint count)
    {
        if (count != 0 && Log.IsEnabled())
            t_mapPackets += count;
    }

    private WorldTickTrace() { }

    // Version 1 appended mapPackets; fields are only ever appended, so a reader by position keeps working.
    [Event(1, Level = EventLevel.Informational, Version = 1)]
    public void InstanceTick(
        long tickId,
        long instanceLow,
        long instanceHigh,
        long mapType,
        long startTimestamp,
        long durationTicks,
        long frequency,
        long completed,
        long broadcast,
        long players,
        long creatures,
        long projectiles,
        long inputTicks,
        long combatTicks,
        long creatureTicks,
        long dirtyTicks,
        long visibilityTicks,
        long trackingTicks,
        long statePreparationTicks,
        long stateEncodingTicks,
        long auraTicks,
        long enqueueTicks,
        long candidateChecks,
        long visiblePairs,
        long dirtyEntities,
        long added,
        long updated,
        long removed,
        long statePackets,
        long stateBytes,
        long mapPackets)
    {
        if (IsEnabled())
        {
            WriteValues(1, stackalloc long[]
            {
                tickId, instanceLow, instanceHigh, mapType, startTimestamp, durationTicks, frequency, completed,
                broadcast, players, creatures, projectiles, inputTicks, combatTicks, creatureTicks, dirtyTicks,
                visibilityTicks, trackingTicks, statePreparationTicks, stateEncodingTicks, auraTicks, enqueueTicks,
                candidateChecks, visiblePairs, dirtyEntities, added, updated, removed, statePackets, stateBytes,
                mapPackets,
            });
        }
    }

    // Version 1 appended sessionPackets.
    [Event(2, Level = EventLevel.Informational, Version = 1)]
    public void WorldTick(long tickId, long durationTicks, long frequency, long completed, long sessionPackets)
    {
        if (IsEnabled())
            WriteValues(2, stackalloc long[] { tickId, durationTicks, frequency, completed, sessionPackets });
    }

    [Event(3, Level = EventLevel.Informational)]
    public void InstanceBatch(long tickId, long durationTicks, long frequency, long instances, long completed)
    {
        if (IsEnabled())
            WriteValues(3, stackalloc long[] { tickId, durationTicks, frequency, instances, completed });
    }

    [NonEvent]
    internal void RecordInstance(in InstanceTickProfile profile, Guid instanceId, int mapType, bool completed)
    {
        long duration = Stopwatch.GetTimestamp() - profile.StartTimestamp;
        Span<byte> id = stackalloc byte[16];
        instanceId.TryWriteBytes(id);
        InstanceTick(TickId, BitConverter.ToInt64(id[..8]), BitConverter.ToInt64(id[8..]), mapType,
            profile.StartTimestamp, duration, Stopwatch.Frequency, completed ? 1 : 0, profile.Broadcast ? 1 : 0,
            profile.Players, profile.Creatures, profile.Projectiles,
            profile.InputTicks, profile.CombatTicks, profile.CreatureTicks, profile.DirtyTicks,
            profile.VisibilityTicks, profile.TrackingTicks, profile.StatePreparationTicks, profile.StateEncodingTicks,
            profile.AuraTicks, profile.EnqueueTicks, profile.CandidateChecks, profile.VisiblePairs,
            profile.DirtyEntities, profile.Added, profile.Updated, profile.Removed, profile.StatePackets, profile.StateBytes,
            profile.MapPackets);
    }

    // Numeric payloads only (InstanceTick's 31 longs the most). Avoid WriteEvent(params object[]) boxing/arrays on the tick.
    [NonEvent]
    private unsafe void WriteValues(int eventId, ReadOnlySpan<long> values)
    {
        EventData* data = stackalloc EventData[values.Length];
        fixed (long* payload = values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                data[i] = default;
                data[i].DataPointer = (IntPtr)(payload + i);
                data[i].Size = sizeof(long);
            }
            WriteEventCore(eventId, values.Length, data);
        }
    }
}
