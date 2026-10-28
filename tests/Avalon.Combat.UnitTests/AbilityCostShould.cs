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
    private static IUnit Caster(PowerType type, uint? power, uint health = 100)
    {
        IUnit unit = Substitute.For<IUnit>();
        unit.PowerType.Returns(type);
        unit.CurrentPower.Returns(power);
        unit.CurrentHealth.Returns(health);
        return unit;
    }

    private static AbilityMetadata Costing(uint cost) => new() { Name = "x", ScriptName = "x", Cost = cost };

    /// <summary>
    /// Mana holding the cost pays; a missing pool counts as empty; only mana pays; a free ability needs no pool. From life
    /// (Blood Price, #1181 Part D), a cost must leave at least 1 life, whatever the mana.
    /// </summary>
    [Theory]
    [InlineData(PowerType.Mana, 30u, 30u, CostCheck.Payable)]
    [InlineData(PowerType.Mana, 29u, 30u, CostCheck.NotEnoughPower)]
    [InlineData(PowerType.Mana, null, 30u, CostCheck.NotEnoughPower)]
    [InlineData(PowerType.None, 1000u, 30u, CostCheck.NotEnoughPower)]
    [InlineData(PowerType.Fury, 1000u, 30u, CostCheck.NotEnoughPower)]
    [InlineData(PowerType.None, null, 0u, CostCheck.Payable)]
    [InlineData(PowerType.Mana, null, 0u, CostCheck.Payable)]
    [InlineData(PowerType.Mana, 0u, 10u, CostCheck.Payable, true, 11u)]
    [InlineData(PowerType.Mana, 0u, 10u, CostCheck.NotEnoughPower, true, 10u)]
    [InlineData(PowerType.Mana, 0u, 0u, CostCheck.Payable, true, 1u)]
    public void Check_a_cost_against_the_casters_mana(PowerType casterPool, uint? power, uint cost, CostCheck expected,
        bool fromLife = false, uint health = 100) =>
        Assert.Equal(expected, AbilityCost.Check(Caster(casterPool, power, health), Costing(cost), fromLife));

    [Fact]
    public void Pay_by_depleting_the_pool()
    {
        IUnit caster = Caster(PowerType.Mana, 100);
        AbilityCost.Pay(caster, Costing(30));
        caster.Received(1).CurrentPower = 70u;

        IUnit bleeder = Caster(PowerType.Mana, 100, health: 50);
        AbilityCost.Pay(bleeder, Costing(10), fromLife: true);
        bleeder.Received(1).CurrentHealth = 40u;
        bleeder.DidNotReceive().CurrentPower = Arg.Any<uint?>();
    }

    [Fact]
    public void Pay_nothing_for_a_free_ability()
    {
        IUnit caster = Caster(PowerType.Mana, 100);
        AbilityCost.Pay(caster, Costing(0));
        caster.DidNotReceive().CurrentPower = Arg.Any<uint?>();
    }
}
