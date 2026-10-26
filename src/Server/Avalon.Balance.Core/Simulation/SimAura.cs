using Avalon.Combat;
using Avalon.Domain.World;

namespace Avalon.Balance.Core;

/// <summary>
/// One aura on a simulated unit, as the server's ActiveAura: its row, who applied it last, its stacks, its snapshot, its
/// schedule, and the fraction of a point its ticks carry.
/// </summary>
public sealed class SimAura(AuraTemplate template, SimUnit? caster, uint stacks, AuraSnapshot snapshot, AuraSchedule schedule)
{
    public AuraTemplate Template { get; } = template;

    public SimUnit? Caster { get; private set; } = caster;

    public uint Stacks { get; private set; } = stacks;

    public AuraSnapshot Snapshot { get; private set; } = snapshot;

    public AuraSchedule Schedule { get; set; } = schedule;

    /// <summary>The fraction of a point its ticks have earned but not yet dealt or healed (AuraRules.TakeTick).</summary>
    public double PeriodicCarry { get; set; }

    /// <summary>
    /// Applied again, as ActiveAura.Renew: the latest caster, the given stacks, a fresh snapshot and schedule, and the
    /// carry started over.
    /// </summary>
    public void Renew(SimUnit? caster, uint stacks, AuraSnapshot snapshot, AuraSchedule schedule)
    {
        Caster = caster;
        Stacks = stacks;
        Snapshot = snapshot;
        Schedule = schedule;
        PeriodicCarry = 0d;
    }
}
