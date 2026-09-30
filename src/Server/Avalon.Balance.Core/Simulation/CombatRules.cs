using Avalon.Combat;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.World.Public.Abilities;

namespace Avalon.Balance.Core;

/// <summary>
/// The server's hit, heal, Fury and haste rules, applied to simulated units. Every number comes from the World
/// functions; only the bookkeeping CombatService does around them (who loses health, who gains power) is here, and
/// the parity test pins it against CombatService.
/// </summary>
public static class CombatRules
{
    /// <summary>An ability's hit: the base (weapon roll first), then dodge, crit, block, armour. The draw order is HitResolver's.</summary>
    public static (uint Damage, HitResult Result) Damage(SimUnit attacker, SimUnit target, SimAbility ability,
        CombatFormula formula, ICombatRandom rng)
    {
        AbilityMetadata m = ability.Metadata;
        float baseDamage = HitResolver.AbilityBase(attacker.Attack, m.EffectValue, m.ScalingStat, m.ScalingCoefficient,
            m.BaseDamageCoefficient, rng);
        return HitResolver.ResolveDamage(attacker.Attack, target.Defence, baseDamage, formula, rng);
    }

    public static (uint Heal, HitResult Result) Heal(SimUnit healer, SimAbility ability, CombatFormula formula, ICombatRandom rng)
    {
        AbilityMetadata m = ability.Metadata;
        float baseHeal = HitResolver.AbilityBase(healer.Attack, m.EffectValue, m.ScalingStat, m.ScalingCoefficient,
            m.BaseDamageCoefficient, rng);
        return HitResolver.ResolveHeal(healer.Attack, baseHeal, formula, rng);
    }

    /// <summary>
    /// A hit on the player, as CombatService.Hit: health lost is at most what it had; a Fury character that survives
    /// gains its share of the health lost (#526), and one the hit kills has its Fury emptied, as the server's IsDead
    /// setter does (ResetFury). Returns the health lost.
    /// </summary>
    public static uint HitPlayer(SimPlayer target, uint damage)
    {
        if (target.IsDead)
            return 0;

        uint before = target.CurrentHealth;
        uint lost = Math.Min(damage, before);
        target.CurrentHealth = before - lost;

        if (target.IsDead)
        {
            if (PowerPool.EmptiesOnReset(target.PowerType))
                target.CurrentPower = 0;
        }
        else if (target.PowerType == PowerType.Fury)
            target.GainPower(Fury.FromDamageTaken(lost, before, target.Health, Fury.DefaultFromDamageTaken));

        return lost;
    }

    /// <summary>
    /// A hit on a creature, as CombatService: the creature loses at most its health; an ability that dealt more than
    /// 0 to a living creature gives the caster its PowerGainPerHit (#526). Returns the health taken.
    /// </summary>
    public static uint HitCreature(SimPlayer attacker, SimCreature target, uint damage, SimAbility ability)
    {
        uint before = target.CurrentHealth;
        if (before == 0)
            return 0;

        uint dealt = Math.Min(damage, before);
        target.CurrentHealth = before - dealt;

        if (damage > 0 && ability.Metadata.PowerGainPerHit > 0)
            attacker.GainPower((uint)ability.Metadata.PowerGainPerHit);

        return dealt;
    }

    /// <summary>A heal, as CombatService.ApplyHeal: nothing on the dead, then HealRules.After. Returns the health restored.</summary>
    public static uint HealPlayer(SimPlayer target, uint heal)
    {
        if (target.IsDead)
            return 0;

        uint before = target.CurrentHealth;
        uint after = HealRules.After(before, target.Health, heal);
        target.CurrentHealth = after;
        return after > before ? after - before : 0;
    }

    /// <summary>As InstanceAbilityCastSystem.CooldownOf: a creature's basic waits its SwingInterval, everything else its cooldown over its haste.</summary>
    public static float CooldownAfterFire(SimUnit caster, SimAbility ability) =>
        caster is SimCreature creature && ReferenceEquals(ability, creature.Basic)
            ? creature.SwingInterval
            : Haste.Scale(ability.Metadata.Cooldown, EffectiveHaste(caster));

    /// <summary>As InstanceAbilityCastSystem: the cast time over the caster's effective haste.</summary>
    public static float CastTime(SimUnit caster, SimAbility ability) => Haste.Scale(ability.Metadata.CastTime, EffectiveHaste(caster));

    /// <summary>
    /// As InstanceAbilityCastSystem.HasteOf: a character's effective haste from its derived stats, a creature's haste
    /// capped by the cap it spawned with.
    /// </summary>
    public static float EffectiveHaste(SimUnit caster) => caster switch
    {
        SimCreature creature => MathF.Min(creature.HastePct, creature.HasteCap),
        _ => caster.HastePct,
    };
}
