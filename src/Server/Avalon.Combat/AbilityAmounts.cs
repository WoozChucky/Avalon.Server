using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Combat;
using Avalon.World.Public.Abilities;

namespace Avalon.Combat;

/// <summary>
/// One ability's advertised per-hit amount (#669) and pace (#1178). A <see cref="Kind" /> of None is "no direct amount",
/// not 0: <see cref="Min" />, <see cref="Max" /> and <see cref="Types" /> then mean nothing, but the pace still does.
/// </summary>
public readonly record struct AbilityAmount(AbilityAmountKind Kind, uint Min, uint Max, DamageTypeMask Types,
    uint CooldownMs, uint CastTimeMs);

/// <summary>
/// The amount a player is told an ability deals or heals per unit hit (#669), from the caster's current
/// combat stats and the ability's own terms, through the very arithmetic a hit uses (#1178): the base
/// (<see cref="OffenceMath.DirectBase" /> for damage, <see cref="OffenceMath.HealBase" /> for a heal) with every roll at
/// the low and then the high end of its inclusive range (<see cref="RollEnds" />), then floored as a hit that nothing
/// procs on, against no defences, would be: <see cref="HitResolver.NormalDamage" /> (a minimum of 1) or
/// <see cref="HitResolver.NormalHeal" />. Damage is typed as a hit reports its types: every type with a part at the high
/// end. With it, the ability's pace (#1178): the cooldown and the cast time a cast would set now, through
/// <see cref="AbilityPace" />, in milliseconds rounded to the nearest, so a client never applies the speed rules itself.
/// </summary>
/// <remarks>
/// Only the generic shape scripts apply an ability's amount directly, through <c>AbilityEffect</c>: an
/// Ally ability heals with it and any other damages. An ability run by any other script may do something
/// else entirely, so its kind is None, and a client shows no line for it.
/// </remarks>
public static class AbilityAmounts
{
    public static AbilityAmount For(in AttackerCombat caster, AbilityMetadata ability, in CasterPace pace)
    {
        uint cooldownMs = Milliseconds(AbilityPace.Cooldown(ability, pace));
        uint castTimeMs = Milliseconds(AbilityPace.CastTime(ability, pace));

        AbilityAmountKind kind = AbilityAmountMath.KindOf(ability.ScriptName, ability.Affects, ability.Effects);
        if (kind == AbilityAmountKind.None)
            return new AbilityAmount(kind, 0, 0, DamageTypeMask.None, cooldownMs, castTimeMs);

        if (kind == AbilityAmountKind.Healing)
        {
            return new AbilityAmount(kind, HitResolver.NormalHeal(OffenceMath.HealBase(caster, ability, RollEnds.Low)),
                HitResolver.NormalHeal(OffenceMath.HealBase(caster, ability, RollEnds.High)), DamageTypeMask.None,
                cooldownMs, castTimeMs);
        }

        DamageParts high = OffenceMath.DirectBase(caster, ability, RollEnds.High);
        return new AbilityAmount(kind, HitResolver.NormalDamage(OffenceMath.DirectBase(caster, ability, RollEnds.Low).Total),
            HitResolver.NormalDamage(high.Total), high.Mask, cooldownMs, castTimeMs);
    }

    public static AbilityAmountInfo ToInfo(IAbility ability, AbilityAmount amount) => new()
    {
        AbilityId = ability.AbilityId.Value,
        Kind = amount.Kind,
        Min = amount.Min,
        Max = amount.Max,
        Types = amount.Types,
        CooldownMs = amount.CooldownMs,
        CastTimeMs = amount.CastTimeMs,
    };

    /// <summary>Seconds as whole milliseconds, rounded to the nearest; a value that is not finite or below 0 is 0.</summary>
    private static uint Milliseconds(float seconds)
    {
        double ms = Math.Round(seconds * 1000d, MidpointRounding.AwayFromZero);
        return double.IsFinite(ms) && ms > 0d ? (ms >= uint.MaxValue ? uint.MaxValue : (uint)ms) : 0u;
    }
}
