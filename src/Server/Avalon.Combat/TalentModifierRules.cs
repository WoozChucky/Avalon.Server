using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;

namespace Avalon.Combat;

/// <summary>
/// The checks on a capstone's modifier (#1181 Part D), shared by the world's talent catalog and the REST API's live
/// template edits, which refuse an ability or aura edit that would make a capstone fail them (the world would then refuse
/// the whole tree). A modifier reshapes one ability, so it is checked against that ability and the ability's aura:
/// <list type="bullet">
/// <item>its numbers are in range: a fan holds at most 9 projectiles, since each lane is an object of its own, and an
/// explosion's radius is 0.1 to 10 metres;</item>
/// <item>it fits the ability's shape: more projectiles and an explosion on a projectile, a wider area on a circle or on a
/// projectile the same capstone explodes, a longer reach or a wider arc on a cone, a second hit on a direct
/// amount;</item>
/// <item>it never lengthens or deepens crowd control: a duration or stack modifier on an aura that stuns, roots, silences
/// or slows is refused.</item>
/// </list>
/// </summary>
public static class TalentModifierRules
{
    /// <summary>The most projectiles a capstone adds: with the ability's own, a fan holds at most 9.</summary>
    public const int MaxAddedProjectiles = 8;

    /// <summary>
    /// Why one of <paramref name="modifiers" /> that shapes <paramref name="ability" /> cannot load, the first in row
    /// order, or null when each can; rows that shape another ability are skipped. A live edit of an ability, or of the
    /// aura an ability applies, runs it over the rows that shape that ability, as the catalog runs each row through
    /// <see cref="RowProblem" />.
    /// </summary>
    public static (TalentAbilityModifier Row, string Problem)? AbilityProblem(
        IReadOnlyList<TalentAbilityModifier> modifiers, AbilityTemplate ability, AuraTemplate? aura)
    {
        foreach (TalentAbilityModifier modifier in modifiers)
        {
            if (modifier.AbilityId.Value == ability.Id.Value
                && RowProblem(modifier, modifiers, ability, aura) is { } problem)
            {
                return (modifier, problem);
            }
        }

        return null;
    }

    /// <summary>
    /// Why <paramref name="modifier" />, one of <paramref name="modifiers" />, cannot shape <paramref name="ability" />,
    /// worded as the catalog refuses the tree ("node N's Kind modifier: ..."), or null when it can. An
    /// <see cref="TalentModifierKind.AreaPct" /> on a projectile needs an <see cref="TalentModifierKind.ImpactExplosion" />
    /// on the same capstone: a fork partner's explosion is never held with it.
    /// </summary>
    public static string? RowProblem(TalentAbilityModifier modifier, IReadOnlyList<TalentAbilityModifier> modifiers,
        AbilityTemplate ability, AuraTemplate? aura)
    {
        bool explodes = false;
        foreach (TalentAbilityModifier other in modifiers)
            explodes |= other.NodeId == modifier.NodeId && other.Kind == TalentModifierKind.ImpactExplosion;

        return Problem(modifier.Kind, modifier.Value, modifier.Value2, ability, aura, explodes) is { } problem
            ? $"node {modifier.NodeId}'s {modifier.Kind} modifier: {problem}"
            : null;
    }

    /// <summary>
    /// Why the modifier cannot load, or null when it can. <paramref name="aura" /> is the ability's aura, null when it
    /// names none or the aura catalog does not hold it; <paramref name="explodes" /> says whether the same capstone gives
    /// the ability an <see cref="TalentModifierKind.ImpactExplosion" />, which gives an
    /// <see cref="TalentModifierKind.AreaPct" /> on a projectile an area to widen.
    /// </summary>
    public static string? Problem(TalentModifierKind kind, int value, int? value2, AbilityTemplate ability,
        AuraTemplate? aura, bool explodes)
    {
        if (!Enum.IsDefined(kind))
            return $"unknown modifier kind {(int)kind}";

        bool explosion = kind == TalentModifierKind.ImpactExplosion;
        if (explosion && value2 is null)
            return "an ImpactExplosion names its effectiveness in Value2";
        if (!explosion && value2 is not null)
            return "only an ImpactExplosion has a Value2";

        int max = kind switch
        {
            TalentModifierKind.AddedProjectiles => MaxAddedProjectiles,
            TalentModifierKind.ImpactExplosion or TalentModifierKind.AuraStacks => 100,
            _ => 1000,
        };
        if (value < 1 || value > max)
            return $"Value {value} is not from 1 to {max}";
        if (value2 is < 1 or > 1000)
            return $"Value2 {value2} is not from 1 to 1000";

        return FitProblem(kind, ability, aura, explodes);
    }

    private static string? FitProblem(TalentModifierKind kind, AbilityTemplate ability, AuraTemplate? aura, bool explodes)
    {
        switch (kind)
        {
            case TalentModifierKind.AddedProjectiles:
                return ability.Shape == AbilityShape.Projectile
                    ? null
                    : $"only a projectile adds projectiles, not a {ability.Shape}";

            case TalentModifierKind.ImpactExplosion:
                if (ability.Shape != AbilityShape.Projectile)
                    return $"only a projectile explodes, not a {ability.Shape}";
                if (ability.Pierce)
                    return "a piercing projectile never ends on the unit it strikes, so it never explodes";
                return ability.Affects == AbilityAffects.Hostile
                       && AbilityRules.HasDirectEffect(ability.Effects, ability.Affects)
                    ? null
                    : "only a projectile that deals damage to hostiles explodes";

            case TalentModifierKind.AreaPct:
                return ability.Shape == AbilityShape.Circle || (ability.Shape == AbilityShape.Projectile && explodes)
                    ? null
                    : "only a circle, or a projectile the same capstone explodes, has an area to widen, "
                      + $"not a {ability.Shape}";

            case TalentModifierKind.ConeReachPct:
            case TalentModifierKind.ConeArcPct:
                return ability.Shape == AbilityShape.Cone
                    ? null
                    : $"only a cone has a reach and an arc to scale, not a {ability.Shape}";

            case TalentModifierKind.AuraStacks:
            case TalentModifierKind.AuraDurationPct:
                if (aura is null)
                    return "the ability applies no aura";
                if (AuraRules.IsCrowdControl(aura))
                    return $"lengthens or deepens crowd control: its aura {aura.Id.Value} stuns, roots, silences or slows";
                return kind == TalentModifierKind.AuraStacks && aura.Stacking != AuraStacking.Stack
                    ? $"its aura {aura.Id.Value} does not stack: its stacking is {aura.Stacking}"
                    : null;

            case TalentModifierKind.SecondHit:
                return AbilityRules.HasDirectEffect(ability.Effects, ability.Affects)
                    ? null
                    : "the ability has no direct amount to land a second time";

            default:
                return $"unknown modifier kind {(int)kind}";
        }
    }
}
