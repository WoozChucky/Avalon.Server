using Avalon.Combat;
using Avalon.Network.Packets.Abilities;
using Avalon.World.Auras;
using Avalon.World.Combat;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Units;

namespace Avalon.World.Abilities.Targeting;

/// <summary>
/// What a skill does to a unit its shape overlaps (#164): a Hostile skill rolls whether it lands on hostile units
/// (#1164) and damages them, an Ally skill heals allies, each only when its Effects carries that direct amount, and then
/// applies its aura. Candidates come from IHitQuery, already alive and nearest-first.
/// </summary>
public static class AbilityEffect
{
    public static bool Qualifies(IAbilityArena arena, IUnit caster, IAbility ability, IUnit unit) =>
        ability.Metadata.Affects == AbilityAffects.Ally
            ? Hostility.IsAlly(caster, unit, arena.MapType)
            : !ArrivalProtection.IsProtected(unit) && Hostility.IsHostile(caster, unit, arena.MapType);

    /// <summary>
    /// What the ability does to one unit its shape affects, then its aura (auras). A Hostile ability rolls whether it lands
    /// (#1164), deals its damage when Effects has Damage, and applies its aura only when it landed: a dodged hit, or one
    /// the target passed over, applies none, whether it deals damage or not. An Unavoidable one skips that check and
    /// offers its aura whatever the hit did (the aura system's own refusals still apply). An Ally ability heals when
    /// Effects has Heal, and is never dodged.
    /// </summary>
    public static void Apply(IAbilityArena arena, IUnit caster, IAbility ability, IUnit unit)
    {
        AbilityMetadata metadata = ability.Metadata;

        if (metadata.Affects == AbilityAffects.Ally)
        {
            if (AbilityRules.HasDirectEffect(metadata.Effects, metadata.Affects))
                arena.HealForAbility(caster, unit, ability);
        }
        else if (arena.HitForAbility(caster, unit, ability) != HitOutcome.Landed
                 && (metadata.Flags & AbilityFlags.Unavoidable) == AbilityFlags.None)
        {
            return;
        }

        arena.ApplyAbilityAura(caster, unit, ability);
    }

    /// <summary>
    /// Applies to each qualifying candidate once, in the order given, except one the arena holds for the dodge wait
    /// (<see cref="IAbilityArena.TryDeferHit" />, #1082), which is applied, or dodged, later. It stops once it has affected
    /// <paramref name="maxTargets" /> (#1181; 0 is no limit): with the hit query's nearest-first order, the nearest that
    /// qualify. Returns how many it affected, those held included.
    /// </summary>
    public static int ApplyToAll(IAbilityArena arena, IUnit caster, IAbility ability, IReadOnlyList<IUnit> candidates,
        int maxTargets)
    {
        int affected = 0;

        // By index: a foreach through the interface boxes the list's enumerator on every cast (#880).
        for (int i = 0; i < candidates.Count; i++)
        {
            IUnit unit = candidates[i];
            if (!Qualifies(arena, caster, ability, unit))
                continue;

            affected++;
            if (!arena.TryDeferHit(caster, ability, unit))
                Apply(arena, caster, ability, unit);

            if (affected == maxTargets)
                break;
        }

        return affected;
    }
}
