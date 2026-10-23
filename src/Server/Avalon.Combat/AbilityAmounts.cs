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
/// combat stats and the ability's own terms, through the very arithmetic a hit uses (#1178): the base
/// (<see cref="OffenceMath.DirectBase" /> for damage, <see cref="OffenceMath.HealBase" /> for a heal) with every roll at
/// the low and then the high end of its inclusive range (<see cref="RollEnds" />), then floored as a hit that nothing
/// procs on, against no defences, would be: <see cref="HitResolver.NormalDamage" /> (a minimum of 1) or
/// <see cref="HitResolver.NormalHeal" />.
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
        AbilityAmountKind kind = AbilityAmountMath.KindOf(ability.ScriptName, ability.Affects, ability.Effects);
        if (kind == AbilityAmountKind.None) return AbilityAmount.None;

        return kind == AbilityAmountKind.Healing
            ? new AbilityAmount(kind, HitResolver.NormalHeal(OffenceMath.HealBase(caster, ability, RollEnds.Low)),
                HitResolver.NormalHeal(OffenceMath.HealBase(caster, ability, RollEnds.High)))
            : new AbilityAmount(kind, HitResolver.NormalDamage(OffenceMath.DirectBase(caster, ability, RollEnds.Low).Total),
                HitResolver.NormalDamage(OffenceMath.DirectBase(caster, ability, RollEnds.High).Total));
    }

    public static AbilityAmountInfo ToInfo(IAbility ability, AbilityAmount amount) => new()
    {
        AbilityId = ability.AbilityId.Value,
        Kind = amount.Kind,
        Min = amount.Min,
        Max = amount.Max,
    };
}
