using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.World.Public.Abilities;
using Xunit;

namespace Avalon.Shared.UnitTests.Domain;

/// <summary>The per-hit arithmetic the world server and the api share (#669, tooltips).</summary>
public class AbilityAmountMathShould
{
    [Theory]
    [InlineData("CircleAbilityScript", AbilityAffects.Hostile, AbilityAmountKind.Damage)]
    [InlineData("ConeAbilityScript", AbilityAffects.Hostile, AbilityAmountKind.Damage)]
    [InlineData("ProjectileAbilityScript", AbilityAffects.Ally, AbilityAmountKind.Healing)]
    [InlineData("ChargeAbilityScript", AbilityAffects.Hostile, AbilityAmountKind.None)]
    [InlineData(null, AbilityAffects.Hostile, AbilityAmountKind.None)]
    public void Name_the_amount_kind_from_the_script_and_affects(string? script, AbilityAffects affects,
        AbilityAmountKind expected) =>
        Assert.Equal(expected, AbilityAmountMath.KindOf(script, affects));

    /// <summary>10 + 0.5 × 40 attack + 1 × (24..28) weapon: 54..58.</summary>
    [Fact]
    public void Range_a_weapon_scaling_ability_over_the_weapon() =>
        Assert.Equal((54u, 58u), AbilityAmountMath.Range(AbilityAmountKind.Damage, 10, ScalingStat.Attack, 0.5f, 1f,
            attackDamage: 40, abilityDamage: 0, weaponMin: 24, weaponMax: 28));

    [Fact]
    public void Scale_with_ability_damage_when_the_stat_is_ability() =>
        Assert.Equal((30u, 30u), AbilityAmountMath.Range(AbilityAmountKind.Healing, 10, ScalingStat.Ability, 2f, 0f,
            attackDamage: 999, abilityDamage: 10, weaponMin: 0, weaponMax: 0));

    [Fact]
    public void Deal_at_least_1_damage_but_heal_0() =>
        Assert.Equal((1u, 0u), (AbilityAmountMath.NormalDamage(0.4), AbilityAmountMath.NormalHeal(0.4)));

    [Fact]
    public void Clamp_a_weapon_minimum_above_its_maximum() =>
        Assert.Equal((5u, 5u), AbilityAmountMath.Range(AbilityAmountKind.Damage, 0, ScalingStat.Attack, 0f, 1f,
            attackDamage: 0, abilityDamage: 0, weaponMin: 9, weaponMax: 5));

    [Fact]
    public void Answer_0_0_for_no_amount() =>
        Assert.Equal((0u, 0u), AbilityAmountMath.Range(AbilityAmountKind.None, 50, ScalingStat.Attack, 1f, 1f,
            attackDamage: 10, abilityDamage: 10, weaponMin: 1, weaponMax: 2));

    /// <summary>0.7f × 10 is 7.0 in float but 6.99999988 in double: the weapon term stays a float product.</summary>
    [Fact]
    public void Multiply_the_weapon_term_in_float() =>
        Assert.Equal(7u, AbilityAmountMath.NormalDamage(AbilityAmountMath.Sum(0, 0, 0, 0.7f, 10)));

    [Fact]
    public void Range_a_fractional_weapon_coefficient_as_combat_does() =>
        Assert.Equal((7u, 7u), AbilityAmountMath.Range(AbilityAmountKind.Damage, 0, ScalingStat.Attack, 0f, 0.7f,
            attackDamage: 0, abilityDamage: 0, weaponMin: 10, weaponMax: 10));
}
