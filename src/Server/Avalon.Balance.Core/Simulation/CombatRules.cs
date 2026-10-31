using Avalon.Combat;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;
using Avalon.World.Public.Abilities;

namespace Avalon.Balance.Core;

/// <summary>What a hit or a tick cost its target (B-R6): what its ward absorbed, and the health it lost.</summary>
public readonly record struct WardedLoss(uint Absorbed, uint Health)
{
    /// <summary>The whole loss, the ward's part included, as the fight's damage totals count it.</summary>
    public uint Total => Absorbed + Health;
}

/// <summary>
/// The server's hit, heal and speed rules, applied to simulated units. Every number comes from the World functions; only
/// the bookkeeping CombatService does around them (the ward first, then who loses health) is here, and the parity test
/// pins it against CombatService.
/// </summary>
public static class CombatRules
{
    /// <summary>
    /// An ability's whole hit: <see cref="Damage(SimUnit, SimUnit, SimAbility, CombatFormula, ICombatRandom, in HitShaping)" />
    /// at <see cref="HitShaping.Whole" />.
    /// </summary>
    public static HitDamage Damage(SimUnit attacker, SimUnit target, SimAbility ability, CombatFormula formula,
        ICombatRandom rng) =>
        Damage(attacker, target, ability, formula, rng, HitShaping.Whole);

    /// <summary>
    /// An ability's hit, as CombatService's: the ability's own base (OffenceMath.DirectBase, its rolls first) at
    /// <paramref name="shaping" />'s share and with its extra tags (B-R15: a second hit, an explosion), then evasion (none
    /// for an Unavoidable ability, #1164), crit, block, mitigation, each rolled afresh. The draw order is HitResolver's.
    /// </summary>
    public static HitDamage Damage(SimUnit attacker, SimUnit target, SimAbility ability, CombatFormula formula,
        ICombatRandom rng, in HitShaping shaping)
    {
        AbilityMetadata m = ability.Metadata;
        DamageParts parts = OffenceMath.DirectBase(attacker.Attack, m, rng, shaping);
        return HitResolver.ResolveDamage(attacker.Attack, target.Defence, parts, formula, rng, IsUnavoidable(m));
    }

    /// <summary>
    /// A direct hit landing, as CombatService.ResolveHit (B-R6): on a target with a ward, the ward takes what the hit lets
    /// it absorb (all of it with <see cref="SimUnit.WardTakesAll" />, else its share that is not blight) and its recharge
    /// pauses; health loses the rest. A hit that kills empties the ward. Nothing lands on the dead.
    /// </summary>
    public static WardedLoss Land(SimUnit target, in HitDamage hit, DateTime now)
    {
        if (target.IsDead)
            return default;

        uint total = hit.Total;
        uint absorbed = 0;
        if (total > 0 && target.MaxWard > 0)
        {
            uint absorbable = target.WardTakesAll ? total : Math.Min(hit.Absorbable, total);
            absorbed = target.AbsorbWithWard(absorbable, now);
            target.WardDamaged(now);
        }

        return new WardedLoss(absorbed, LoseHealth(target, total - absorbed, now));
    }

    /// <summary>
    /// An aura's damage tick landing, as CombatService.ApplyPeriodicDamage (B-R6): on a target with a ward, the ward takes
    /// the tick only when HitResolver.WardAbsorbs allows its <paramref name="type" /> (never a bleed's or blight's, but
    /// with <see cref="SimUnit.WardTakesAll" />), and any tick pauses its recharge; health loses the rest. A tick that kills
    /// empties the ward.
    /// </summary>
    public static WardedLoss LandTick(SimUnit target, uint points, DamageType type, DateTime now)
    {
        if (target.IsDead)
            return default;

        uint absorbed = 0;
        if (points > 0 && target.MaxWard > 0)
        {
            absorbed = HitResolver.WardAbsorbs(type, periodic: true, target.WardTakesAll)
                ? target.AbsorbWithWard(points, now)
                : 0u;
            target.WardDamaged(now);
        }

        return new WardedLoss(absorbed, LoseHealth(target, points - absorbed, now));
    }

    /// <summary>
    /// What passed the ward, taken from health. Death empties the ward on both sides: a player's as CharacterEntity's
    /// IsDead does, a creature's as Creature's CurrentHealth does on reaching 0.
    /// </summary>
    private static uint LoseHealth(SimUnit target, uint damage, DateTime now)
    {
        uint lost = target switch
        {
            SimPlayer player => HitPlayer(player, damage),
            SimCreature creature => HitCreature(creature, damage),
            _ => 0u,
        };
        if (target.IsDead)
            target.EmptyWard(now);
        return lost;
    }

    /// <summary>
    /// Whether a Hostile ability that deals no damage lands on <paramref name="target" />, as CombatService.ApplyAbilityHit
    /// (#1164): the evasion roll alone, against the <paramref name="attacker" />'s level, none for an Unavoidable ability.
    /// </summary>
    public static bool Lands(SimUnit attacker, SimUnit target, SimAbility ability, CombatFormula formula,
        ICombatRandom rng) =>
        IsUnavoidable(ability.Metadata) || !HitResolver.Evades(target.Defence, attacker.Attack.Level, formula, rng);

    private static bool IsUnavoidable(AbilityMetadata m) => (m.Flags & AbilityFlags.Unavoidable) != AbilityFlags.None;

    /// <summary>
    /// An ability's heal, as CombatService.ApplyHeal: its heal base at <paramref name="amountPct" /> % (B-R15: a second
    /// heal), then its own crit roll.
    /// </summary>
    public static (uint Heal, HitResult Result) Heal(SimUnit healer, SimAbility ability, CombatFormula formula,
        ICombatRandom rng, float amountPct = 100f)
    {
        double baseHeal = OffenceMath.HealBase(healer.Attack, ability.Metadata, rng, amountPct);
        return HitResolver.ResolveHeal(healer.Attack, baseHeal, formula, rng);
    }

    /// <summary>
    /// A hit on the player, as CombatService.Hit: health lost is at most what it had; mana is left alone (#1181).
    /// Returns the health lost.
    /// </summary>
    private static uint HitPlayer(SimPlayer target, uint damage)
    {
        if (target.IsDead)
            return 0;

        uint before = target.CurrentHealth;
        uint lost = Math.Min(damage, before);
        target.CurrentHealth = before - lost;
        return lost;
    }

    /// <summary>
    /// A hit on a creature, as CombatService, and an aura's damage tick on one, as CombatService.ApplyPeriodicDamage: the
    /// creature loses at most its health; no hit gives power (#1181). Returns the health taken.
    /// </summary>
    private static uint HitCreature(SimCreature target, uint damage)
    {
        uint before = target.CurrentHealth;
        if (before == 0)
            return 0;

        uint dealt = Math.Min(damage, before);
        target.CurrentHealth = before - dealt;
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

    /// <summary>As InstanceAbilityCastSystem.CooldownOf: AbilityPace.Cooldown over the caster's pace, a creature's basic its SwingInterval.</summary>
    public static float CooldownAfterFire(SimUnit caster, SimAbility ability) =>
        AbilityPace.Cooldown(ability.Metadata, PaceOf(caster),
            creatureBasic: caster is SimCreature creature && ReferenceEquals(ability, creature.Basic));

    /// <summary>As InstanceAbilityCastSystem: the cast time over the caster's cast speed.</summary>
    public static float CastTime(SimUnit caster, SimAbility ability) => AbilityPace.CastTime(ability.Metadata, PaceOf(caster));

    /// <summary>As InstanceAbilityCastSystem.PaceOf: a character's pace from its derived stats, a creature's capped by its caps.</summary>
    public static CasterPace PaceOf(SimUnit caster) => caster switch
    {
        SimCreature creature => creature.Pace,
        SimPlayer player => player.Pace,
        _ => default,
    };
}
