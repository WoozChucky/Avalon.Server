using Avalon.Combat;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Server.World.UnitTests.Combat;
using Avalon.World.Combat;
using Avalon.World.Public.Abilities;
using Avalon.World.Scripts.Abilities;
using Xunit;

namespace Avalon.Server.World.UnitTests.Abilities;

/// <summary>
/// The per-hit amount a player is told an ability deals or heals (#669): the base combat computes, at both
/// ends of the weapon roll, floored as a hit that nothing procs on, against no armour. The acceptance
/// examples of the issue, one per test.
/// </summary>
public class AbilityAmountsShould
{
    private static AttackerCombat Caster(uint attack = 0, uint ability = 0, uint weaponMin = 0, uint weaponMax = 0) =>
        new(Level: 1, AttackDamage: attack, AbilityDamage: ability, CritPct: 50f, WeaponMin: weaponMin, WeaponMax: weaponMax);

    private static AbilityMetadata Ability(uint effect = 0, ScalingStat stat = ScalingStat.Attack, float scaling = 0f,
        float weapon = 0f, AbilityAffects affects = AbilityAffects.Hostile, string script = "ConeAbilityScript") => new()
    {
        Name = "Test", ScriptName = script, EffectValue = effect, ScalingStat = stat, ScalingCoefficient = scaling,
        BaseDamageCoefficient = weapon, Affects = affects,
        Effects = affects == AbilityAffects.Ally
            ? Avalon.World.Public.Enums.SpellEffect.Heal
            : Avalon.World.Public.Enums.SpellEffect.Damage,
    };

    /// <summary>10 + 0.5 × 40 attack + 1 × (24..28) weapon: 54..58, and it follows the weapon and the stat.</summary>
    [Fact]
    public void Follow_a_weapon_scaling_abilitys_weapon_and_attack_damage()
    {
        AbilityMetadata cleave = Ability(effect: 10, scaling: 0.5f, weapon: 1f);

        Assert.Equal(new AbilityAmount(AbilityAmountKind.Damage, 54, 58),
            AbilityAmounts.For(Caster(attack: 40, weaponMin: 24, weaponMax: 28), cleave));
        Assert.Equal(new AbilityAmount(AbilityAmountKind.Damage, 60, 70),
            AbilityAmounts.For(Caster(attack: 40, weaponMin: 30, weaponMax: 40), cleave));
        Assert.Equal(new AbilityAmount(AbilityAmountKind.Damage, 59, 63),
            AbilityAmounts.For(Caster(attack: 50, weaponMin: 24, weaponMax: 28), cleave));
    }

    /// <summary>A spell with no weapon term is one number, whatever is in the main hand, and follows ability damage.</summary>
    [Fact]
    public void Follow_a_spells_ability_damage_without_a_weapon_roll()
    {
        AbilityMetadata bolt = Ability(effect: 12, stat: ScalingStat.Ability, scaling: 0.8f);

        Assert.Equal(new AbilityAmount(AbilityAmountKind.Damage, 28, 28),
            AbilityAmounts.For(Caster(attack: 99, ability: 20, weaponMin: 5, weaponMax: 50), bolt));
        Assert.Equal(new AbilityAmount(AbilityAmountKind.Damage, 36, 36),
            AbilityAmounts.For(Caster(attack: 99, ability: 30, weaponMin: 5, weaponMax: 50), bolt));
    }

    /// <summary>An ally ability heals, before the crit, floored with no minimum of 1.</summary>
    [Fact]
    public void Label_an_ally_heal_as_healing()
    {
        AbilityMetadata mend = Ability(effect: 40, stat: ScalingStat.Ability, scaling: 0.55f, affects: AbilityAffects.Ally,
            script: "CircleAbilityScript");

        Assert.Equal(new AbilityAmount(AbilityAmountKind.Healing, 51, 51), AbilityAmounts.For(Caster(ability: 21), mend));
        Assert.Equal(new AbilityAmount(AbilityAmountKind.Healing, 0, 0),
            AbilityAmounts.For(Caster(), Ability(affects: AbilityAffects.Ally, script: "CircleAbilityScript")));
    }

    /// <summary>A hit never deals less than 1, so an ability with no terms at all still says 1.</summary>
    [Fact]
    public void Floor_damage_at_one_as_a_hit_does()
    {
        Assert.Equal(new AbilityAmount(AbilityAmountKind.Damage, 1, 1), AbilityAmounts.For(Caster(), Ability()));
    }

    /// <summary>An ability run by any script other than the shape scripts gets no fabricated amount.</summary>
    [Fact]
    public void State_no_amount_for_a_script_that_does_not_apply_one_directly()
    {
        Assert.Equal(AbilityAmount.None,
            AbilityAmounts.For(Caster(attack: 40), Ability(effect: 10, scaling: 1f, script: "SomeCustomScript")));
    }

    [Theory]
    [InlineData("CircleAbilityScript")]
    [InlineData("ConeAbilityScript")]
    [InlineData("ProjectileAbilityScript")]
    public void State_an_amount_for_every_shape_script(string script)
    {
        Assert.Equal(AbilityAmountKind.Damage, AbilityAmounts.For(Caster(), Ability(effect: 5, script: script)).Kind);
    }

    /// <summary>
    /// The bounds are what a real hit resolves to from the lowest and highest weapon roll, when nothing procs
    /// and the target has no armour, for damage and for healing alike.
    /// </summary>
    [Fact]
    public void Match_what_a_hit_resolves_to_at_each_end_of_the_weapon_roll()
    {
        AttackerCombat caster = Caster(attack: 33, ability: 17, weaponMin: 9, weaponMax: 14);
        CombatFormula formula = Avalon.Database.World.Seeding.CombatSeed.Formula();

        AbilityMetadata hit = Ability(effect: 7, scaling: 0.37f, weapon: 1.3f);
        AbilityAmount amount = AbilityAmounts.For(caster, hit);

        foreach ((long roll, uint expected) in new[] { (9L, amount.Min), (14L, amount.Max) })
        {
            float b = HitResolver.AbilityBase(caster, hit.EffectValue, hit.ScalingStat, hit.ScalingCoefficient,
                hit.BaseDamageCoefficient, new ScriptedCombatRandom().Longs(roll));
            (uint dealt, _) = HitResolver.ResolveDamage(caster, new DefenderCombat(0, 0f, 0f), b, formula,
                new ScriptedCombatRandom().Doubles(0.99, 0.99, 0.99));
            Assert.Equal(expected, dealt);

            (uint healed, _) = HitResolver.ResolveHeal(caster, b, formula, new ScriptedCombatRandom().Doubles(0.99));
            Assert.Equal(HitResolver.NormalHeal(b), healed);
        }
    }

    [Fact]
    public void Share_the_direct_script_names_with_the_api() =>
        Assert.Equal(
            new[] { nameof(CircleAbilityScript), nameof(ConeAbilityScript), nameof(ProjectileAbilityScript) }.Order(),
            AbilityAmountMath.DirectScripts.Order());

    /// <summary>The world server and the api must print the same numbers for the same inputs.</summary>
    [Theory]
    [InlineData(0u, 0.35f, 0.7f, AbilityAffects.Hostile, 37u, 10u, 10u)]
    [InlineData(5u, 0.35f, 0.7f, AbilityAffects.Ally, 41u, 3u, 17u)]
    [InlineData(10u, 0.5f, 1f, AbilityAffects.Hostile, 40u, 24u, 28u)]
    [InlineData(7u, 0.35f, 0f, AbilityAffects.Ally, 13u, 0u, 0u)]
    [InlineData(0u, 0f, 0.7f, AbilityAffects.Hostile, 0u, 9u, 5u)]
    public void Match_the_shared_api_arithmetic(uint effect, float scaling, float weapon, AbilityAffects affects,
        uint attack, uint weaponMin, uint weaponMax)
    {
        AbilityMetadata ability = Ability(effect: effect, scaling: scaling, weapon: weapon, affects: affects,
            script: "ProjectileAbilityScript");
        AbilityAmount amount = AbilityAmounts.For(Caster(attack: attack, weaponMin: weaponMin, weaponMax: weaponMax),
            ability);

        AbilityAmountKind kind = AbilityAmountMath.KindOf(ability.ScriptName, affects, ability.Effects);
        (uint min, uint max) = AbilityAmountMath.Range(kind, effect, ScalingStat.Attack, scaling, weapon, attack, 0,
            weaponMin, weaponMax);

        Assert.Equal(new AbilityAmount(kind, min, max), amount);
    }
}
