using Avalon.Network.Packets.Abilities;
using Avalon.World.Public.Enums;

namespace Avalon.Combat.UnitTests;

/// <summary>What the world server and the api tell a player an ability does (#669, tooltips).</summary>
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
        Assert.Equal(expected, AbilityAmountMath.KindOf(script, affects, SpellEffect.Damage | SpellEffect.Heal));

    [Fact]
    public void Deal_at_least_1_damage_but_heal_0() =>
        Assert.Equal((1u, 0u), (AbilityAmountMath.NormalDamage(0.4), AbilityAmountMath.NormalHeal(0.4)));
}
