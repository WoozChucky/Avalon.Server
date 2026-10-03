using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.World.Public.Enums;

namespace Avalon.Combat.UnitTests;

/// <summary>Effects gates an ability's direct amount: Damage for a Hostile ability, Heal for an Ally one.</summary>
public class AbilityEffectRulesShould
{
    [Theory]
    [InlineData(SpellEffect.Damage, AbilityAffects.Hostile, true)]
    [InlineData(SpellEffect.Debuff, AbilityAffects.Hostile, false)]
    [InlineData(SpellEffect.Heal, AbilityAffects.Hostile, false)]
    [InlineData(SpellEffect.Heal | SpellEffect.Buff, AbilityAffects.Ally, true)]
    [InlineData(SpellEffect.Buff, AbilityAffects.Ally, false)]
    public void Deal_or_heal_directly_only_when_Effects_says_so(SpellEffect effects, AbilityAffects affects, bool direct) =>
        Assert.Equal(direct, AbilityRules.HasDirectEffect(effects, affects));

    private static AbilityTemplate Ignite(AuraId? aura) => new()
    {
        Id = new AbilityId(213), Name = "Ignite", ScriptName = AbilityRules.CircleScript, Shape = AbilityShape.Circle,
        AimMode = AbilityAimMode.Cursor, Anchor = AbilityAnchor.AimPoint, Reach = 18f, Radius = 3f,
        Effects = SpellEffect.Debuff, AuraId = aura,
    };

    [Fact]
    public void Accept_an_aura_only_ability() => Assert.Null(AbilityRules.Problem(Ignite(new AuraId(2))));

    [Fact]
    public void Refuse_an_ability_that_does_nothing() =>
        Assert.Equal("the ability does nothing: Effects has no Damage for a Hostile ability, and it applies no aura",
            AbilityRules.Problem(Ignite(null)));

    [Theory]
    [InlineData(SpellEffect.Debuff, AbilityAffects.Hostile, AbilityAmountKind.None)]
    [InlineData(SpellEffect.Damage, AbilityAffects.Hostile, AbilityAmountKind.Damage)]
    [InlineData(SpellEffect.Buff, AbilityAffects.Ally, AbilityAmountKind.None)]
    [InlineData(SpellEffect.Heal, AbilityAffects.Ally, AbilityAmountKind.Healing)]
    public void Advertise_no_amount_for_an_ability_that_deals_none(SpellEffect effects, AbilityAffects affects,
        AbilityAmountKind expected) =>
        Assert.Equal(expected, AbilityAmountMath.KindOf(AbilityRules.CircleScript, affects, effects));
}
