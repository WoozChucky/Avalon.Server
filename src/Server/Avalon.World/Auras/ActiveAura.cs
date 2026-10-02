using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;

namespace Avalon.World.Auras;

/// <summary>
/// One aura on one unit: the template it was applied with (kept, so a reload never changes an aura already held), who
/// applied it, its stacks, its snapshot and its schedule. Tick thread only. World-side.
/// </summary>
public sealed class ActiveAura(
    AuraTemplate template,
    ObjectGuid casterGuid,
    AuraSource source,
    uint stacks,
    AuraSnapshot snapshot,
    AuraSchedule schedule,
    uint durationMs,
    DateTime appliedAt)
{
    /// <summary>Unique among the auras its unit holds, given by UnitAuras; the client's handle for it.</summary>
    public uint Key { get; internal set; }

    public AuraTemplate Template { get; private set; } = template;

    public AuraId Id => Template.Id;

    /// <summary>Who applied it last; raw 0 for nobody.</summary>
    public ObjectGuid CasterGuid { get; private set; } = casterGuid;

    public AuraSource Source { get; private set; } = source;

    public uint Stacks { get; private set; } = stacks;

    public AuraSnapshot Snapshot { get; private set; } = snapshot;

    public AuraSchedule Schedule { get; set; } = schedule;

    /// <summary>Its whole duration when last applied, in milliseconds: what a client draws its timer against.</summary>
    public uint DurationMs { get; private set; } = durationMs;

    /// <summary>When it was last applied, UTC.</summary>
    public DateTime AppliedAt { get; private set; } = appliedAt;

    /// <summary>Its script asked to end it; the aura system removes it once the hook returns.</summary>
    public bool ScriptEnded { get; set; }

    /// <summary>
    /// The fraction of a point its ticks have earned but not yet dealt or healed, carried from tick to tick so the ticks
    /// add up to the snapshot's total (AuraRules.TakeTick). Saved and restored with the aura; a refresh starts it over.
    /// </summary>
    public double PeriodicCarry { get; set; }

    /// <summary>
    /// Applied again: the current template, the latest caster, a fresh snapshot and schedule and the given stacks. A
    /// refresh is a new application in all but its key.
    /// </summary>
    public void Renew(AuraTemplate current, ObjectGuid caster, AuraSource from, uint newStacks, AuraSnapshot newSnapshot,
        DateTimeOffset now)
    {
        Template = current;
        CasterGuid = caster;
        Source = from;
        Stacks = newStacks;
        Snapshot = newSnapshot;
        Schedule = AuraSchedule.Start(now, current.DurationMs, current.TickIntervalMs);
        DurationMs = current.DurationMs;
        AppliedAt = now.UtcDateTime;
        PeriodicCarry = 0d;
    }
}
