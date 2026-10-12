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
#pragma warning restore IDE1006

    internal static long TickId { get => t_tickId; set => t_tickId = value; }

    private WorldTickTrace() { }

    [Event(1, Level = EventLevel.Informational)]
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
        long stateBytes)
    {
        if (IsEnabled())
        {
            WriteValues(1, stackalloc long[]
            {
                tickId, instanceLow, instanceHigh, mapType, startTimestamp, durationTicks, frequency, completed,
                broadcast, players, creatures, projectiles, inputTicks, combatTicks, creatureTicks, dirtyTicks,
                visibilityTicks, trackingTicks, statePreparationTicks, stateEncodingTicks, auraTicks, enqueueTicks,
                candidateChecks, visiblePairs, dirtyEntities, added, updated, removed, statePackets, stateBytes,
            });
        }
    }

    [Event(2, Level = EventLevel.Informational)]
    public void WorldTick(long tickId, long durationTicks, long frequency, long completed)
    {
        if (IsEnabled())
            WriteValues(2, stackalloc long[] { tickId, durationTicks, frequency, completed });
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
            profile.DirtyEntities, profile.Added, profile.Updated, profile.Removed, profile.StatePackets, profile.StateBytes);
    }

    // Numeric payloads only, at most 30 longs. Avoid WriteEvent(params object[]) boxing/arrays on the tick.
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
