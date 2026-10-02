using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.World.Public.Abilities;

namespace Avalon.Combat;

/// <summary>The aura rules the world server and the balance simulator share. Pure.</summary>
public static class AuraRules
{
    /// <summary>
    /// Why a row cannot load, or null when it can, its numbers written in the invariant culture. A script name is
    /// checked by the world, which knows them.
    /// </summary>
    public static string? Problem(AuraTemplate t)
    {
        if (!Enum.IsDefined(t.Kind)) return $"unknown kind {(int)t.Kind}";
        if (!Enum.IsDefined(t.PeriodicKind)) return $"unknown periodic kind {(int)t.PeriodicKind}";
        if (!Enum.IsDefined(t.Stacking)) return $"unknown stacking {(int)t.Stacking}";
        if (!Enum.IsDefined(t.ScalingStat)) return $"unknown scaling stat {(int)t.ScalingStat}";
        if (t.DurationMs == 0) return "DurationMs must be above 0";
        if (t.PeriodicKind != AuraPeriodicKind.None && t.TickIntervalMs == 0)
            return "a periodic aura needs a TickIntervalMs above 0";
        if (t.TickIntervalMs > t.DurationMs)
            return $"TickIntervalMs {t.TickIntervalMs} is longer than DurationMs {t.DurationMs}";
        if (t.MaxStacks < 1) return "MaxStacks must be 1 or more";

        foreach ((string name, float value) in new[]
                 {
                     ("PeriodicBase", t.PeriodicBase), ("ScalingCoefficient", t.ScalingCoefficient),
                     ("BaseDamageCoefficient", t.BaseDamageCoefficient),
                 })
        {
            if (!float.IsFinite(value) || value < 0f)
                return FormattableString.Invariant($"{name} {value} is not a finite value of 0 or more");
        }

        if (t.Kind == AuraKind.Harmful && t.PeriodicKind == AuraPeriodicKind.Heal) return "a harmful aura cannot heal";
        if (t.Kind == AuraKind.Helpful && t.PeriodicKind == AuraPeriodicKind.Damage) return "a helpful aura cannot deal damage";

        var seen = new HashSet<AuraStat>();
        foreach (AuraStatModifier m in t.Modifiers)
        {
            if (!Enum.IsDefined(m.Stat)) return $"unknown stat {(int)m.Stat}";
            if (!Enum.IsDefined(m.Kind)) return $"unknown modifier kind {(int)m.Kind} on {m.Stat}";
            if (!float.IsFinite(m.Value)) return FormattableString.Invariant($"{m.Stat} modifier {m.Value} is not finite");
            // A percentage at or below -100 would take the whole stat, or turn it negative.
            if (m.Kind == AuraModifierKind.Percent && m.Value <= -100f) return FormattableString.Invariant($"{m.Stat} modifier {m.Value} % would take the whole stat");
            if (!seen.Add(m.Stat)) return $"{m.Stat} is modified twice";
        }

        return null;
    }

    /// <summary>
    /// How many ticks an aura gives: one per whole interval in its duration, at least one for a tick-giving aura, none
    /// with no interval. The ticks are anchored at expiry (see <see cref="AuraSchedule" />), so the last lands there.
    /// </summary>
    public static int TickCount(uint durationMs, uint tickIntervalMs) =>
        tickIntervalMs == 0 ? 0 : (int)Math.Max(1u, durationMs / tickIntervalMs);

    /// <summary>
    /// The snapshot: <c>PeriodicBase + ScalingCoefficient x the caster's ScalingStat + BaseDamageCoefficient x one
    /// roll of the caster's base damage</c>, the ability formula (<see cref="HitResolver.AbilityBase" />), divided
    /// evenly over the ticks. The roll is uniform and inclusive over the caster's WeaponMin..WeaponMax (a character's
    /// main hand, a creature's natural range) and is drawn once, here, only for a tick-giving aura whose coefficient
    /// is above 0 and whose caster has a range above 0. A creature has no damage stats, so its auras scale through
    /// the roll alone.
    /// </summary>
    public static AuraSnapshot Snapshot(AuraTemplate t, in AttackerCombat caster, ICombatRandom rng)
    {
        int ticks = TickCount(t.DurationMs, t.TickIntervalMs);
        if (t.PeriodicKind == AuraPeriodicKind.None || ticks == 0)
            return new AuraSnapshot(0f, caster.CritPct, (ushort)Math.Min(caster.Level, ushort.MaxValue));

        float total = HitResolver.AbilityBase(caster, t.PeriodicBase, t.ScalingStat, t.ScalingCoefficient,
            t.BaseDamageCoefficient, rng);
        return new AuraSnapshot(total / ticks, caster.CritPct, (ushort)Math.Min(caster.Level, ushort.MaxValue));
    }

    /// <summary>
    /// The whole points one tick deals or heals: <paramref name="amount" /> (an unfloored
    /// <see cref="HitResolver.ResolvePeriodic" /> or <see cref="HitResolver.ResolvePeriodicHeal" />) is added to the aura's
    /// <paramref name="carry" /> and the whole points are taken out, as regeneration takes them
    /// (<see cref="PowerRegen.TakeWholePoints" />), the fraction left for the next tick. There is no minimum of 1: a tick
    /// below a point gives nothing until the carry reaches one. On the <paramref name="lastTick" /> the leftover is
    /// rounded to the nearest point, a half up, and the carry emptied, so the ticks add up to the total rounded. An
    /// amount that is not a finite value of 0 or more counts as nothing. The carry lives with the aura that ticks.
    /// </summary>
    public static uint TakeTick(double amount, ref double carry, bool lastTick)
    {
        double earned = double.IsFinite(amount) && amount > 0d ? amount : 0d;
        uint whole = PowerRegen.TakeWholePoints(earned, ref carry);
        if (!lastTick) return whole;

        if (carry >= 0.5d && whole < uint.MaxValue) whole++;
        carry = 0d;
        return whole;
    }

    /// <summary>The stacks after the aura is applied again: one more for a Stack aura, up to its cap; 1 otherwise.</summary>
    public static uint NextStacks(AuraStacking stacking, uint current, uint maxStacks) =>
        stacking == AuraStacking.Stack ? Math.Min(current + 1, Math.Max(1u, maxStacks)) : 1u;

    /// <summary>An Independent aura keeps one copy per caster; every other kind keeps one copy per unit.</summary>
    public static bool KeysByCaster(AuraStacking stacking) => stacking == AuraStacking.Independent;

    /// <summary>A Hostile ability applies only harmful auras, an Ally one only helpful auras.</summary>
    public static bool Fits(AuraKind kind, AbilityAffects affects) =>
        affects == AbilityAffects.Ally ? kind == AuraKind.Helpful : kind == AuraKind.Harmful;
}
