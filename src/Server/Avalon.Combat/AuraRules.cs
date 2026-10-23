using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Combat;

namespace Avalon.Combat;

/// <summary>The aura rules the world server and the balance simulator share. Pure.</summary>
public static class AuraRules
{
    private const CrowdControl KnownControl = CrowdControl.Stun | CrowdControl.Root | CrowdControl.Silence;

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
        if ((t.Control & ~KnownControl) != 0) return $"unknown control {(int)t.Control}";
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
                     ("PeriodicEffectiveness", t.PeriodicEffectiveness),
                 })
        {
            if (!float.IsFinite(value) || value < 0f)
                return FormattableString.Invariant($"{name} {value} is not a finite value of 0 or more");
        }

        foreach ((string name, float value) in new[]
                 {
                     ("BreakOnDamagePct", t.BreakOnDamagePct), ("BreakPerSecondPct", t.BreakPerSecondPct),
                 })
        {
            if (!float.IsFinite(value) || value < 0f || value > 100f)
                return FormattableString.Invariant($"{name} {value} is not a finite percentage from 0 to 100");
        }

        if (t.Kind == AuraKind.Harmful && t.PeriodicKind == AuraPeriodicKind.Heal) return "a harmful aura cannot heal";
        if (t.Kind == AuraKind.Helpful && t.PeriodicKind == AuraPeriodicKind.Damage) return "a helpful aura cannot deal damage";
        if (t.Kind == AuraKind.Helpful && t.Control != CrowdControl.None)
            return "a helpful aura cannot stun, root or silence";

        var seen = new HashSet<AuraStat>();
        foreach (AuraStatModifier m in t.Modifiers)
        {
            if (!Enum.IsDefined(m.Stat)) return $"unknown stat {(int)m.Stat}";
            if (!Enum.IsDefined(m.Kind)) return $"unknown modifier kind {(int)m.Kind} on {m.Stat}";
            if (!float.IsFinite(m.Value)) return FormattableString.Invariant($"{m.Stat} modifier {m.Value} is not finite");
            // An Increased or a More at or below -100 would take the whole stat, or turn it negative.
            if (m.Kind is AuraModifierKind.Increased or AuraModifierKind.More && m.Value <= -100f)
                return FormattableString.Invariant($"{m.Stat} modifier {m.Value} % would take the whole stat");
            // The Increased keys are percentages already: "+X % increased" is Flat and "X % more" is More.
            if (m.Kind == AuraModifierKind.Increased && m.Stat is >= AuraStat.IncreasedDamage and <= AuraStat.IncreasedHealing)
                return $"{m.Stat} is already a percentage: it takes Flat or More, never Increased";
            if (!seen.Add(m.Stat)) return $"{m.Stat} is modified twice";
        }

        return null;
    }

    /// <summary>
    /// A slow: a Harmful aura with a negative MovementSpeed modifier (#770). Slow is derived, never a Control bit, so
    /// an existing slowing debuff joins crowd control with no data change. Reads <see cref="AuraTemplate.Modifiers" />.
    /// </summary>
    public static bool IsSlow(AuraTemplate t)
    {
        if (t.Kind != AuraKind.Harmful) return false;

        foreach (AuraStatModifier m in t.Modifiers)
        {
            if (m.Stat == AuraStat.MovementSpeed && m.Value < 0f) return true;
        }

        return false;
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
    /// the roll alone. <paramref name="type" /> is the type its damage ticks deal (#1178), which the caller resolves from
    /// the source ability and the caster (<see cref="HitResolver.TypeOf" />).
    /// </summary>
    public static AuraSnapshot Snapshot(AuraTemplate t, in AttackerCombat caster, DamageType type, ICombatRandom rng)
    {
        ushort level = (ushort)Math.Min(caster.Level, ushort.MaxValue);
        int ticks = TickCount(t.DurationMs, t.TickIntervalMs);
        if (t.PeriodicKind == AuraPeriodicKind.None || ticks == 0)
            return new AuraSnapshot(0f, caster.CritPct, level, type);

        float total = HitResolver.AbilityBase(caster, t.PeriodicBase, t.ScalingStat, t.ScalingCoefficient,
            t.BaseDamageCoefficient, rng);
        return new AuraSnapshot(total / ticks, caster.CritPct, level, type);
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

    /// <summary>
    /// Why an ability's aura link cannot load, or null when it can: an ability naming no aura is fine; one naming an
    /// aura that <paramref name="findAura" /> does not return (missing, or refused by its own checks) is refused, and
    /// so is one whose aura does not <see cref="Fits" /> what the ability affects. <paramref name="findAura" /> answers
    /// the loaded aura row for an id, or null.
    /// </summary>
    public static string? LinkProblem(AbilityAffects affects, AuraId? auraId, Func<AuraId, AuraTemplate?> findAura)
    {
        if (auraId is null) return null;

        if (findAura(auraId) is not { } aura)
            return $"names aura {auraId.Value}, which is missing or refused";

        return FitProblem(affects, aura);
    }

    /// <summary>
    /// The second half of <see cref="LinkProblem" />, for a caller that already holds the loaded aura row: why an
    /// ability that affects <paramref name="affects" /> cannot apply <paramref name="aura" />, or null when it
    /// <see cref="Fits" />. It takes no delegate, so asking it for every application allocates nothing.
    /// </summary>
    public static string? FitProblem(AbilityAffects affects, AuraTemplate aura) =>
        Fits(aura.Kind, affects)
            ? null
            : $"its aura {aura.Id.Value} '{aura.Name}' is {aura.Kind}, which {(affects == AbilityAffects.Ally ? "an Ally" : "a Hostile")} ability cannot apply";

    /// <summary>
    /// Why <paramref name="ability" /> cannot apply <paramref name="aura" /> for its damage type (#1178), or null: a class
    /// ability whose aura deals damage over time names the type the ticks deal (Ignite says Fire), as one dealing damage
    /// directly must. A creature-only ability may leave it null, and its aura then deals the creature's base type.
    /// </summary>
    public static string? DamageTypeProblem(AbilityTemplate ability, AuraTemplate aura) =>
        ability.DamageType is null && ability.AllowedClasses is { Count: > 0 } && aura.PeriodicKind == AuraPeriodicKind.Damage
            ? "a class ability that applies a damaging aura names its damage type"
            : null;

    /// <summary>
    /// Why <paramref name="ability" /> cannot be the source of <paramref name="aura" /> under the offence model (#1178), or
    /// null: a damage-over-time aura scales with its source's tags, so a class ability applying a damaging aura is an
    /// Attack or a Spell and an Ally ability applying a healing aura is a Heal; and a Spell or a Heal whose aura ticks
    /// has a spell base for the aura to draw on, as one dealing or healing directly must.
    /// </summary>
    public static string? SourceProblem(AbilityTemplate ability, AuraTemplate aura)
    {
        AbilityTags tags = ability.Tags;
        if (aura.PeriodicKind == AuraPeriodicKind.Damage && ability.AllowedClasses is { Count: > 0 }
            && !AbilityRules.Has(tags, AbilityTags.Attack | AbilityTags.Spell))
        {
            return "a class ability that applies a damaging aura is an Attack or a Spell";
        }

        if (aura.PeriodicKind == AuraPeriodicKind.Heal && ability.Affects == AbilityAffects.Ally
            && !AbilityRules.Has(tags, AbilityTags.Heal))
        {
            return "an Ally ability that applies a healing aura is a Heal";
        }

        return aura.PeriodicKind != AuraPeriodicKind.None && AbilityRules.Has(tags, AbilityTags.Spell | AbilityTags.Heal)
               && ability.SpellBaseMax == 0
            ? AbilityRules.NoSpellBase
            : null;
    }
}
