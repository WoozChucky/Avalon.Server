using System.Diagnostics;

namespace Avalon.World.Telemetry;

/// <summary>Reused, tick-owned phase totals. No clocks or counting loops run while the trace is disabled.</summary>
internal struct InstanceTickProfile
{
    public bool Enabled;
    public bool Broadcast;
    public long StartTimestamp;
    public long Players;
    public long Creatures;
    public long Projectiles;
    public long InputTicks;
    public long CombatTicks;
    public long CreatureTicks;
    public long DirtyTicks;
    public long VisibilityTicks;
    public long TrackingTicks;
    public long StatePreparationTicks;
    public long StateEncodingTicks;
    public long AuraTicks;
    public long EnqueueTicks;
    public long CandidateChecks;
    public long VisiblePairs;
    public long DirtyEntities;
    public long Added;
    public long Updated;
    public long Removed;
    public long StatePackets;
    public long StateBytes;

    public readonly long Timestamp() => Enabled ? Stopwatch.GetTimestamp() : 0;
    public readonly long Elapsed(long start) => Enabled ? Stopwatch.GetTimestamp() - start : 0;
}
