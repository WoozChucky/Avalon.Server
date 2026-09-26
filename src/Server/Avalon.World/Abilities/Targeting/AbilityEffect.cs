using Avalon.Network.Packets.Abilities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Units;

namespace Avalon.World.Abilities.Targeting;

/// <summary>
/// What a skill does to a unit its shape overlaps (#164): a Hostile skill damages hostile units, an
/// Ally skill heals allies. Candidates come from IHitQuery, already alive and nearest-first.
/// </summary>
public static class AbilityEffect
{
    public static bool Qualifies(IAbilityArena arena, IUnit caster, IAbility ability, IUnit unit) =>
        ability.Metadata.Affects == AbilityAffects.Ally
            ? Hostility.IsAlly(caster, unit, arena.MapType)
            : Hostility.IsHostile(caster, unit, arena.MapType);

    public static void Apply(IAbilityArena arena, IUnit caster, IAbility ability, IUnit unit)
    {
        if (ability.Metadata.Affects == AbilityAffects.Ally)
            arena.CombatService.ApplyHeal(caster, unit, ability.Metadata.EffectValue, ability);
        else
            arena.CombatService.ApplyDamage(caster, unit, ability.Metadata.EffectValue, ability);
    }

    /// <summary>Applies to each qualifying candidate once, in the order given. Returns how many it affected.</summary>
    public static int ApplyToAll(IAbilityArena arena, IUnit caster, IAbility ability, IReadOnlyList<IUnit> candidates)
    {
        int affected = 0;
        foreach (IUnit unit in candidates)
        {
            if (!Qualifies(arena, caster, ability, unit))
                continue;

            Apply(arena, caster, ability, unit);
            affected++;
        }

        return affected;
    }
}
