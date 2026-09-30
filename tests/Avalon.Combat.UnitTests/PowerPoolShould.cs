using Avalon.Network.Packets.State;
using Avalon.Combat;

namespace Avalon.Combat.UnitTests;

/// <summary>#526: a gain is capped at the maximum, refused to the dead and to a pool no cast spends; Fury alone resets.</summary>
public class PowerPoolShould
{
    [Theory]
    [InlineData(PowerType.Mana)]
    [InlineData(PowerType.Energy)]
    [InlineData(PowerType.Fury)]
    public void Add_the_gain_to_a_pool_a_cast_spends(PowerType pool)
    {
        Assert.Equal(18u, PowerPool.Gain(pool, dead: false, current: 10, max: 100, amount: 8));
    }

    [Fact]
    public void Cap_the_gain_at_the_maximum()
    {
        Assert.Equal(100u, PowerPool.Gain(PowerType.Fury, dead: false, current: 97, max: 100, amount: 8));
        Assert.Equal(uint.MaxValue, PowerPool.Gain(PowerType.Fury, false, uint.MaxValue - 1, uint.MaxValue, uint.MaxValue));
    }

    [Fact]
    public void Add_nothing_for_a_gain_of_zero()
    {
        Assert.Equal(10u, PowerPool.Gain(PowerType.Fury, dead: false, current: 10, max: 100, amount: 0));
    }

    [Fact]
    public void Leave_a_pool_above_its_maximum_as_it_is()
    {
        Assert.Equal(120u, PowerPool.Gain(PowerType.Mana, dead: false, current: 120, max: 100, amount: 8));
    }

    [Fact]
    public void Give_the_dead_nothing()
    {
        Assert.Equal(10u, PowerPool.Gain(PowerType.Fury, dead: true, current: 10, max: 100, amount: 8));
    }

    [Fact]
    public void Give_a_pool_no_cast_spends_nothing()
    {
        Assert.Equal(10u, PowerPool.Gain(PowerType.None, dead: false, current: 10, max: 100, amount: 8));
        Assert.Equal(10u, PowerPool.Gain((PowerType)99, dead: false, current: 10, max: 100, amount: 8));
    }

    [Fact]
    public void Empty_only_fury_on_a_reset()
    {
        Assert.True(PowerPool.EmptiesOnReset(PowerType.Fury));
        Assert.False(PowerPool.EmptiesOnReset(PowerType.Mana));
        Assert.False(PowerPool.EmptiesOnReset(PowerType.Energy));
    }
}
