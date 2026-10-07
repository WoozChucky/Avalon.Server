using Avalon.Network.Packets.State;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Units;
using NSubstitute;

namespace Avalon.Combat.UnitTests;

/// <summary>
/// #521 item 2: one power rule, used by the handler to check and by the cast system to pay, on both paths. #652: a
/// cost is spent from the pool the ability names, never from whichever pool the caster happens to have.
/// </summary>
public class AbilityCostShould
{
    private static IUnit Caster(PowerType type, uint? power)
    {
        var unit = Substitute.For<IUnit>();
        unit.PowerType.Returns(type);
        unit.CurrentPower.Returns(power);
        return unit;
    }

    private static AbilityMetadata Costing(uint cost, PowerType pool = PowerType.None) =>
        new() { Name = "x", ScriptName = "x", Cost = cost, CostPowerType = pool };

    /// <summary>A matching pool pays when it holds the cost, Fury like Mana and Energy (#526).</summary>
    [Theory]
    [InlineData(PowerType.Mana, 30u, CostCheck.Payable)]
    [InlineData(PowerType.Energy, 30u, CostCheck.Payable)]
    [InlineData(PowerType.Fury, 30u, CostCheck.Payable)]
    [InlineData(PowerType.Mana, 29u, CostCheck.NotEnoughPower)]
    [InlineData(PowerType.Energy, 29u, CostCheck.NotEnoughPower)]
    [InlineData(PowerType.Fury, 29u, CostCheck.NotEnoughPower)]
    public void Check_a_cost_against_a_matching_pool(PowerType pool, uint power, CostCheck expected) =>
        Assert.Equal(expected, AbilityCost.Check(Caster(pool, power), Costing(30, pool)));

    /// <summary>#652: points in another pool never pay, however many; a caster with no pool pays nothing either.</summary>
    [Theory]
    [InlineData(PowerType.Fury, PowerType.Mana)]
    [InlineData(PowerType.Energy, PowerType.Mana)]
    [InlineData(PowerType.Mana, PowerType.Fury)]
    [InlineData(PowerType.Energy, PowerType.Fury)]
    [InlineData(PowerType.Mana, PowerType.Energy)]
    [InlineData(PowerType.Fury, PowerType.Energy)]
    [InlineData(PowerType.None, PowerType.Mana)]
    public void Refuse_a_cost_spent_from_another_pool(PowerType casterPool, PowerType costPool) =>
        Assert.Equal(CostCheck.WrongPowerType, AbilityCost.Check(Caster(casterPool, 1000), Costing(30, costPool)));

    /// <summary>A cost that names no pool is a data fault (the catalog and the database refuse it), never a free cast.</summary>
    [Theory]
    [InlineData(PowerType.Mana)]
    [InlineData(PowerType.None)]
    public void Refuse_a_cost_that_names_no_pool(PowerType casterPool) =>
        Assert.Equal(CostCheck.NoCostPowerType, AbilityCost.Check(Caster(casterPool, 1000), Costing(30)));

    [Fact]
    public void Count_a_missing_pool_as_empty() =>
        Assert.Equal(CostCheck.NotEnoughPower, AbilityCost.Check(Caster(PowerType.Mana, null), Costing(30, PowerType.Mana)));

    /// <summary>A free ability needs no pool and names none: any caster casts it, one with PowerType.None included.</summary>
    [Theory]
    [InlineData(PowerType.None)]
    [InlineData(PowerType.Fury)]
    [InlineData(PowerType.Mana)]
    [InlineData(PowerType.Energy)]
    public void Let_a_free_ability_through_whatever_the_power_type(PowerType type) =>
        Assert.Equal(CostCheck.Payable, AbilityCost.Check(Caster(type, null), Costing(0)));

    [Theory]
    [InlineData(PowerType.Mana)]
    [InlineData(PowerType.Energy)]
    [InlineData(PowerType.Fury)]
    public void Pay_by_depleting_the_pool(PowerType type)
    {
        IUnit caster = Caster(type, 100);
        AbilityCost.Pay(caster, Costing(30, type));
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
