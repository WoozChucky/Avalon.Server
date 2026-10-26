using Avalon.Network.Packets.State;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Units;
using NSubstitute;

namespace Avalon.Combat.UnitTests;

/// <summary>
/// #521 item 2: one power rule, used by the handler to check and by the cast system to pay, on both paths. #1181: every
/// cost is spent from mana; a caster on any other pool, or none, cannot pay one.
/// </summary>
public class AbilityCostShould
{
    private static IUnit Caster(PowerType type, uint? power)
    {
        IUnit unit = Substitute.For<IUnit>();
        unit.PowerType.Returns(type);
        unit.CurrentPower.Returns(power);
        return unit;
    }

    private static AbilityMetadata Costing(uint cost) => new() { Name = "x", ScriptName = "x", Cost = cost };

    /// <summary>Mana holding the cost pays; a missing pool counts as empty; only mana pays; a free ability needs no pool.</summary>
    [Theory]
    [InlineData(PowerType.Mana, 30u, 30u, CostCheck.Payable)]
    [InlineData(PowerType.Mana, 29u, 30u, CostCheck.NotEnoughPower)]
    [InlineData(PowerType.Mana, null, 30u, CostCheck.NotEnoughPower)]
    [InlineData(PowerType.None, 1000u, 30u, CostCheck.NotEnoughPower)]
    [InlineData(PowerType.Fury, 1000u, 30u, CostCheck.NotEnoughPower)]
    [InlineData(PowerType.None, null, 0u, CostCheck.Payable)]
    [InlineData(PowerType.Mana, null, 0u, CostCheck.Payable)]
    public void Check_a_cost_against_the_casters_mana(PowerType casterPool, uint? power, uint cost, CostCheck expected) =>
        Assert.Equal(expected, AbilityCost.Check(Caster(casterPool, power), Costing(cost)));

    [Fact]
    public void Pay_by_depleting_the_pool()
    {
        IUnit caster = Caster(PowerType.Mana, 100);
        AbilityCost.Pay(caster, Costing(30));
        caster.Received(1).CurrentPower = 70u;
    }

    [Fact]
    public void Pay_nothing_for_a_free_ability()
    {
        IUnit caster = Caster(PowerType.Mana, 100);
        AbilityCost.Pay(caster, Costing(0));
        caster.DidNotReceive().CurrentPower = Arg.Any<uint?>();
    }
}
