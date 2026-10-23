using Avalon.Combat;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Combat;
using Avalon.Server.World.UnitTests.Combat;
using Avalon.World.Public.Abilities;
using Avalon.World.Scripts.Abilities;

namespace Avalon.Server.World.UnitTests.Abilities;

/// <summary>
/// The per-hit amount a player is told an ability deals or heals (#669): the base combat computes (#1178: an Attack's
/// weapon roll, a Spell's or a Heal's spell base, scaled by the caster's offence), with every roll at its low and at its
/// high end, floored as a hit that nothing procs on, against no armour.
/// </summary>
public class AbilityAmountsShould
{
    private static AttackerCombat Caster(uint weaponMin = 0, uint weaponMax = 0, OffenceStats offence = default) =>
        new(Level: 1, CritPct: 50f, Weapon: new WeaponDamage(weaponMin, weaponMax, DamageType.Physical, 0, 0, null, 800),
            Offence: offence);

    private static AbilityMetadata Ability(AbilityTags tags = AbilityTags.Attack, float effectiveness = 100f,
        uint baseMin = 0, uint baseMax = 0, AbilityAffects affects = AbilityAffects.Hostile,
        string script = "ConeAbilityScript") => new()
        {
            Name = "Test",
            ScriptName = script,
            Tags = tags,
            DamageType = affects == AbilityAffects.Ally ? null : DamageType.Physical,
            DamageEffectiveness = effectiveness,
            SpellBaseMin = baseMin,
            SpellBaseMax = baseMax,
            Affects = affects,
            Effects = affects == AbilityAffects.Ally
            ? Avalon.World.Public.Enums.SpellEffect.Heal
            : Avalon.World.Public.Enums.SpellEffect.Damage,
        };

    /// <summary>
    /// An ally ability heals its spell base times its increased healing (20..30 × 1.5), before the crit, floored with no
    /// minimum of 1.
    /// </summary>
    [Fact]
    public void Label_an_ally_heal_as_healing()
    {
        OffenceStats healing = default;
        healing.AddIncreased(OffenceStat.Healing, 50f);
        AbilityMetadata mend = Ability(AbilityTags.Heal | AbilityTags.Area, baseMin: 20, baseMax: 30,
            affects: AbilityAffects.Ally, script: "CircleAbilityScript");

        Assert.Equal(new AbilityAmount(AbilityAmountKind.Healing, 30, 45, DamageTypeMask.None, 0, 0),
            AbilityAmounts.For(Caster(offence: healing), mend, default));
        Assert.Equal(new AbilityAmount(AbilityAmountKind.Healing, 0, 0, DamageTypeMask.None, 0, 0),
            AbilityAmounts.For(Caster(), Ability(AbilityTags.Heal, affects: AbilityAffects.Ally, script: "CircleAbilityScript"),
                default));
    }

    /// <summary>A hit never deals less than 1, so an ability with nothing to roll still says 1.</summary>
    [Fact]
    public void Floor_damage_at_one_as_a_hit_does()
    {
        Assert.Equal(new AbilityAmount(AbilityAmountKind.Damage, 1, 1, DamageTypeMask.None, 0, 0),
            AbilityAmounts.For(Caster(), Ability(), default));
    }

    /// <summary>An ability run by any script other than the shape scripts gets no fabricated amount.</summary>
    [Fact]
    public void State_no_amount_for_a_script_that_does_not_apply_one_directly()
    {
        Assert.Equal(new AbilityAmount(AbilityAmountKind.None, 0, 0, DamageTypeMask.None, 0, 0),
            AbilityAmounts.For(Caster(10, 20), Ability(effectiveness: 300f, script: "SomeCustomScript"), default));
    }

    /// <summary>
    /// The bounds are what a real hit resolves to from the lowest and highest weapon roll, when nothing procs
    /// and the target has no armour, for damage and for healing alike.
    /// </summary>
    [Fact]
    public void Match_what_a_hit_resolves_to_at_each_end_of_the_weapon_roll()
    {
        OffenceStats offence = default;
        offence.AddIncreased(OffenceStat.PhysicalAttack, 37f);
        AttackerCombat caster = Caster(9, 14, offence);
        CombatFormula formula = Avalon.Database.World.Seeding.CombatSeed.Formula();

        AbilityMetadata hit = Ability(effectiveness: 130f);
        AbilityAmount amount = AbilityAmounts.For(caster, hit, default);

        foreach ((long roll, uint expected) in new[] { (9L, amount.Min), (14L, amount.Max) })
        {
            DamageParts parts = OffenceMath.DirectBase(caster, hit, new ScriptedCombatRandom().Longs(roll));
            uint dealt = HitResolver.ResolveDamage(caster, new DefenderCombat(0, 0, 0f), parts, formula,
                new ScriptedCombatRandom().Doubles(0.99, 0.99, 0.99)).Total;
            Assert.Equal(expected, dealt);
        }
    }

    [Fact]
    public void Share_the_direct_script_names_with_the_api() =>
        Assert.Equal(
            new[] { nameof(CircleAbilityScript), nameof(ConeAbilityScript), nameof(ProjectileAbilityScript) }.Order(),
            AbilityAmountMath.DirectScripts.Order());
}
