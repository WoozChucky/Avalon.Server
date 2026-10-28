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
    /// <summary>
    /// Whether the ability may affect <paramref name="unit" />: an Ally ability an ally; a Hostile one a hostile unit that
    /// hits do not pass over (<see cref="CombatService.IgnoresHits" />: an invulnerable creature, a corpse, a creature
    /// walking home or a protected arrival), so such a unit takes no target slot and a projectile flies through it (#1181).
    /// </summary>
    public static bool Qualifies(IAbilityArena arena, IUnit caster, IAbility ability, IUnit unit) =>
        ability.Metadata.Affects == AbilityAffects.Ally
            ? Hostility.IsAlly(caster, unit, arena.MapType)
            : !CombatService.IgnoresHits(unit) && Hostility.IsHostile(caster, unit, arena.MapType);

    /// <summary>
    /// What the ability does to one unit its shape affects, then its aura (auras). A Hostile ability rolls whether it lands
    /// (#1164), deals its damage when Effects has Damage, and applies its aura only when it landed: a dodged hit, or one
    /// the target passed over, applies none, whether it deals damage or not. An Unavoidable one skips that check and
    /// offers its aura whatever the hit did (the aura system's own refusals still apply). An Ally ability heals when
    /// Effects has Heal, and is never dodged.
    /// </summary>
    /// <remarks>
    /// A second hit (#1181 Part D: <see cref="AbilityMetadata.SecondHitEffectiveness" /> above 0) follows at once, at
    /// that share of the amount, with its own evasion, crit and block rolls: an Ally ability heals again, then applies
    /// its aura once. A Hostile one applies its aura after the first hit when that landed (or it is Unavoidable), then
    /// hits again unless the first was passed over or left the unit dead, so a corpse draws no roll; when the first
    /// applied no aura and the second lands, the second applies it. Only the first landed hit applies the aura.
    /// </remarks>
    public static void Apply(IAbilityArena arena, IUnit caster, IAbility ability, IUnit unit)
    {
        AbilityMetadata metadata = ability.Metadata;
        float secondHit = metadata.SecondHitEffectiveness;

        if (metadata.Affects == AbilityAffects.Ally)
        {
            if (AbilityRules.HasDirectEffect(metadata.Effects, metadata.Affects))
            {
                arena.HealForAbility(caster, unit, ability, 100f);
                if (secondHit > 0f)
                    arena.HealForAbility(caster, unit, ability, secondHit);
            }

            arena.ApplyAbilityAura(caster, unit, ability);
            return;
        }

        HitOutcome first = arena.HitForAbility(caster, unit, ability, HitShaping.Whole);
        bool auraApplied = first == HitOutcome.Landed
                           || (metadata.Flags & AbilityFlags.Unavoidable) != AbilityFlags.None;
        if (auraApplied)
            arena.ApplyAbilityAura(caster, unit, ability);

        if (!(secondHit > 0f) || first == HitOutcome.Ignored || AuraSystem.IsDead(unit))
            return;

        HitOutcome second = arena.HitForAbility(caster, unit, ability, new HitShaping(secondHit, AbilityTags.None));
        if (!auraApplied && second == HitOutcome.Landed)
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
