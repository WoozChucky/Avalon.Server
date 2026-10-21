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
    /// When it next rolls its chance to break on its own (#770, <see cref="AuraTemplate.BreakPerSecondPct" />): a whole
    /// second after it was applied, then every second after, on the instance's clock. Moved on with its schedule while
    /// its time stands still, past the rolls an empty instance skipped, and set from the time a restored aura had held.
    /// </summary>
    public DateTimeOffset NextBreakRollAt { get; internal set; } =
        schedule.ExpiresAt - TimeSpan.FromMilliseconds(durationMs) + CrowdControlRules.BreakRollInterval;

    /// <summary>
    /// Applied again: the current template, the latest caster, a fresh snapshot, the given stacks, and a fresh schedule
    /// of <paramref name="durationMs" /> from now (the template's own, or a crowd-control aura's after its resistance or
    /// diminishing, or what it had left when that was longer), its rolls to break on its own counted from now. A refresh
    /// is a new application in all but its key.
    /// </summary>
    public void Renew(AuraTemplate current, ObjectGuid caster, AuraSource from, uint newStacks, AuraSnapshot newSnapshot,
        uint durationMs, DateTimeOffset now)
    {
        Template = current;
        CasterGuid = caster;
        Source = from;
        Stacks = newStacks;
        Snapshot = newSnapshot;
        Schedule = AuraSchedule.Start(now, durationMs, current.TickIntervalMs);
        DurationMs = durationMs;
        AppliedAt = now.UtcDateTime;
        PeriodicCarry = 0d;
        NextBreakRollAt = now + CrowdControlRules.BreakRollInterval;
    }

    /// <summary>
    /// After a stretch when no pass ran (#770): the rolls to break on its own that fell before <paramref name="now" />
    /// are skipped, the next keeping its place on the whole seconds since it was applied; one at now is kept.
    /// </summary>
    internal void SkipBreakRollsBefore(DateTimeOffset now)
    {
        if (NextBreakRollAt >= now)
            return;

        long step = CrowdControlRules.BreakRollInterval.Ticks;
        long behind = (now - NextBreakRollAt).Ticks;
        NextBreakRollAt += TimeSpan.FromTicks((behind + step - 1) / step * step);
    }
}
