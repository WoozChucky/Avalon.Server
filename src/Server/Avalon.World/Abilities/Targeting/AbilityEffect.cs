using Avalon.Combat;
using Avalon.Network.Packets.Abilities;
using Avalon.World.Combat;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Units;

namespace Avalon.World.Abilities.Targeting;

/// <summary>
/// What a skill does to a unit its shape overlaps (#164): a Hostile skill damages hostile units, an
/// Ally skill heals allies, each only when its Effects carries that direct amount, and then applies its
/// aura. Candidates come from IHitQuery, already alive and nearest-first.
/// </summary>
public static class AbilityEffect
{
    public static bool Qualifies(IAbilityArena arena, IUnit caster, IAbility ability, IUnit unit) =>
        ability.Metadata.Affects == AbilityAffects.Ally
            ? Hostility.IsAlly(caster, unit, arena.MapType)
            : Hostility.IsHostile(caster, unit, arena.MapType);

    /// <summary>
    /// What the ability does to one unit its shape affects: its direct amount when Effects has one for what it affects
    /// (Damage on a hostile unit, Heal on an ally), then its aura (auras). A dodged hit, or one the target passed over,
    /// applies no aura; an ability with no direct amount only applies its aura, so it is never dodged.
    /// </summary>
    public static void Apply(IAbilityArena arena, IUnit caster, IAbility ability, IUnit unit)
    {
        AbilityMetadata metadata = ability.Metadata;
        bool direct = AbilityRules.HasDirectEffect(metadata.Effects, metadata.Affects);

        if (metadata.Affects == AbilityAffects.Ally)
        {
            if (direct)
                arena.HealForAbility(caster, unit, ability);
        }
        else if (direct && arena.DamageForAbility(caster, unit, ability) != HitOutcome.Landed)
        {
            return;
        }

        arena.ApplyAbilityAura(caster, unit, ability);
    }

    /// <summary>Applies to each qualifying candidate once, in the order given. Returns how many it affected.</summary>
    public static int ApplyToAll(IAbilityArena arena, IUnit caster, IAbility ability, IReadOnlyList<IUnit> candidates)
    {
        int affected = 0;

        // By index: a foreach through the interface boxes the list's enumerator on every cast (#880).
        for (int i = 0; i < candidates.Count; i++)
        {
            IUnit unit = candidates[i];
            if (!Qualifies(arena, caster, ability, unit))
                continue;

            Apply(arena, caster, ability, unit);
            affected++;
        }

        return affected;
    }
}
