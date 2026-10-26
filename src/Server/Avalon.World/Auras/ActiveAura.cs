using System.Diagnostics.CodeAnalysis;
using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;

namespace Avalon.World.Auras;

/// <summary>
/// One aura on one unit: the template it was applied with (kept, so a reload never changes an aura already held), who
/// applied it, its stacks, its snapshot and its schedule. Tick thread only. World-side. A copy an aura system placed
/// comes from that system's pool and goes back to it once that system removes it (<see cref="AuraSystem" />); any other
/// copy is left to the collector. So a copy is only ever read while a unit holds it, or by the caller that just removed
/// it, within the tick.
/// </summary>
public sealed class ActiveAura
{
    // Whom a pooled copy names as its caster: nobody, one guid for every pooled copy, so clearing one allocates nothing.
    private static readonly ObjectGuid s_nobody = new();

    public ActiveAura(AuraTemplate template, ObjectGuid casterGuid, AuraSource source, uint stacks, AuraSnapshot snapshot,
        AuraSchedule schedule, uint durationMs, DateTime appliedAt)
    {
        Start(template, casterGuid, source, stacks, snapshot, schedule, durationMs, appliedAt);
    }

    /// <summary>Unique among the auras its unit holds, given by UnitAuras; the client's handle for it.</summary>
    public uint Key { get; internal set; }

    public AuraTemplate Template { get; private set; }

    public AuraId Id => Template.Id;

    /// <summary>Who applied it last; raw 0 for nobody.</summary>
    public ObjectGuid CasterGuid { get; private set; }

    public AuraSource Source { get; private set; }

    public uint Stacks { get; private set; }

    public AuraSnapshot Snapshot { get; private set; }

    public AuraSchedule Schedule { get; set; }

    /// <summary>Its whole duration when last applied, in milliseconds: what a client draws its timer against.</summary>
    public uint DurationMs { get; private set; }

    /// <summary>When it was last applied, UTC.</summary>
    public DateTime AppliedAt { get; private set; }

    /// <summary>
    /// The crowd control it puts its holder in, as the wire carries it (#770, <see cref="AuraWire.ControlOf" />):
    /// worked out from its template here and on <see cref="Renew" />, never per packet.
    /// </summary>
    public uint WireControl { get; private set; }

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
    public DateTimeOffset NextBreakRollAt { get; internal set; }

    /// <summary>
    /// The aura system that made it and pools it, set once when it made it; null for a copy made anywhere else (restored at
    /// login), which no pool takes back. A copy is pooled only by its own instance, so a long-lived instance that ends
    /// copies made elsewhere (a town ending a forest's buffs) never keeps them.
    /// </summary>
    internal AuraSystem? Pool { get; init; }

    /// <summary>Removed and given back to its aura system's pool, until the pool hands it out again.</summary>
    internal bool Returned { get; set; }

    /// <summary>
    /// A pooled copy placed again as a new aura: every field as a new copy of these values has it, its key given again by
    /// the unit that adds it.
    /// </summary>
    internal void Reuse(AuraTemplate template, ObjectGuid casterGuid, AuraSource source, uint stacks, AuraSnapshot snapshot,
        AuraSchedule schedule, uint durationMs, DateTime appliedAt)
    {
        Key = 0;
        Returned = false;
        Start(template, casterGuid, source, stacks, snapshot, schedule, durationMs, appliedAt);
    }

    /// <summary>
    /// Joining its pool: every reference it holds is let go (its template, its caster's guid, its source and through it the
    /// ability's id), so a pooled copy keeps nothing alive, an old template after a reload included. Nothing reads a
    /// pooled copy; its template is null until <see cref="Reuse" />.
    /// </summary>
    internal void Clear()
    {
        Template = null!;
        CasterGuid = s_nobody;
        Source = default;
        Snapshot = default;
        Schedule = default;
    }

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
        WireControl = AuraWire.ControlOf(current);
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

    [MemberNotNull(nameof(Template))]
    private void Start(AuraTemplate template, ObjectGuid casterGuid, AuraSource source, uint stacks, AuraSnapshot snapshot,
        AuraSchedule schedule, uint durationMs, DateTime appliedAt)
    {
        Template = template;
        CasterGuid = casterGuid;
        Source = source;
        Stacks = stacks;
        Snapshot = snapshot;
        Schedule = schedule;
        DurationMs = durationMs;
        AppliedAt = appliedAt;
        WireControl = AuraWire.ControlOf(template);
        ScriptEnded = false;
        PeriodicCarry = 0d;
        NextBreakRollAt = schedule.ExpiresAt - TimeSpan.FromMilliseconds(durationMs) + CrowdControlRules.BreakRollInterval;
    }
}
