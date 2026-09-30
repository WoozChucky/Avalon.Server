using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Character;
using Avalon.World.Public.Abilities;

namespace Avalon.Combat;

/// <summary>One ability's advertised per-hit amount (#669). <see cref="None" /> is "no direct amount", not 0.</summary>
public readonly record struct AbilityAmount(AbilityAmountKind Kind, uint Min, uint Max)
{
    public static readonly AbilityAmount None = new(AbilityAmountKind.None, 0, 0);
}

/// <summary>
/// The amount a player is told an ability deals or heals per unit hit (#669), from the caster's current
/// combat stats and the ability's own terms, through <see cref="HitResolver" />'s own arithmetic: the base
/// (<c>EffectValue + ScalingCoefficient × stat + BaseDamageCoefficient × weapon roll</c>) at both ends of
/// the weapon's inclusive range, then floored as a hit that nothing procs on, against no armour, would be:
/// <see cref="HitResolver.NormalDamage" /> (a minimum of 1) or <see cref="HitResolver.NormalHeal" />.
/// </summary>
/// <remarks>
/// Only the generic shape scripts apply an ability's amount directly, through <c>AbilityEffect</c>: an
/// Ally ability heals with it and any other damages. An ability run by any other script may do something
/// else entirely, so it is <see cref="AbilityAmount.None" />, and a client shows no line for it.
/// </remarks>
public static class AbilityAmounts
{
    public static AbilityAmount For(in AttackerCombat caster, AbilityMetadata ability)
    {
        AbilityAmountKind kind = AbilityAmountMath.KindOf(ability.ScriptName, ability.Affects);
        if (kind == AbilityAmountKind.None) return AbilityAmount.None;

        (float low, float high) = HitResolver.AbilityBaseRange(caster, ability.EffectValue, ability.ScalingStat,
            ability.ScalingCoefficient, ability.BaseDamageCoefficient);

        return kind == AbilityAmountKind.Healing
            ? new AbilityAmount(kind, HitResolver.NormalHeal(low), HitResolver.NormalHeal(high))
            : new AbilityAmount(kind, HitResolver.NormalDamage(low), HitResolver.NormalDamage(high));
    }

    public static AbilityAmountInfo ToInfo(IAbility ability, AbilityAmount amount) => new()
    {
        AbilityId = ability.AbilityId.Value,
        Kind = amount.Kind,
        Min = amount.Min,
        Max = amount.Max,
    };
}
