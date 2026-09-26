using Avalon.Network.Packets.State;
using Avalon.World.Abilities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Units;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Abilities;

/// <summary>#521 item 2: one power rule, used by the handler to check and by the cast system to pay, on both paths.</summary>
public class AbilityCostShould
{
    private static IUnit Caster(PowerType type, uint? power)
    {
        var unit = Substitute.For<IUnit>();
        unit.PowerType.Returns(type);
        unit.CurrentPower.Returns(power);
        return unit;
    }

    private static AbilityMetadata Costing(uint cost) => new() { Name = "x", ScriptName = "x", Cost = cost };

    /// <summary>Fury is a pool casts spend, like Mana and Energy (#526); only None has nothing to spend.</summary>
    [Theory]
    [InlineData(PowerType.Mana, 30u, CostCheck.Payable)]
    [InlineData(PowerType.Energy, 30u, CostCheck.Payable)]
    [InlineData(PowerType.Fury, 30u, CostCheck.Payable)]
    [InlineData(PowerType.Mana, 29u, CostCheck.NotEnoughPower)]
    [InlineData(PowerType.Energy, 29u, CostCheck.NotEnoughPower)]
    [InlineData(PowerType.Fury, 29u, CostCheck.NotEnoughPower)]
    [InlineData(PowerType.None, 100u, CostCheck.WrongPowerType)]
    public void Check_a_cost_against_the_power_type_and_pool(PowerType type, uint power, CostCheck expected) =>
        Assert.Equal(expected, AbilityCost.Check(Caster(type, power), Costing(30)));

    [Fact]
    public void Count_a_missing_pool_as_empty() =>
        Assert.Equal(CostCheck.NotEnoughPower, AbilityCost.Check(Caster(PowerType.Mana, null), Costing(30)));

    [Theory]
    [InlineData(PowerType.None)]
    [InlineData(PowerType.Fury)]
    [InlineData(PowerType.Mana)]
    public void Let_a_free_ability_through_whatever_the_power_type(PowerType type) =>
        Assert.Equal(CostCheck.Payable, AbilityCost.Check(Caster(type, null), Costing(0)));

    [Theory]
    [InlineData(PowerType.Mana)]
    [InlineData(PowerType.Energy)]
    [InlineData(PowerType.Fury)]
    public void Pay_by_depleting_the_pool(PowerType type)
    {
        IUnit caster = Caster(type, 100);
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
