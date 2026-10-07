using Avalon.Network.Packets.State;

namespace Avalon.Combat.UnitTests;

/// <summary>#526: a gain is capped at the maximum, refused to the dead and to a pool no cast spends; Fury alone resets.</summary>
public class PowerPoolShould
{
    [Theory]
    [InlineData(PowerType.Mana, false, 10u, 100u, 8u, 18u)]                                        // a pool a cast spends
    [InlineData(PowerType.Energy, false, 10u, 100u, 8u, 18u)]
    [InlineData(PowerType.Fury, false, 10u, 100u, 8u, 18u)]
    [InlineData(PowerType.Fury, false, 97u, 100u, 8u, 100u)]                                       // capped at the maximum
    [InlineData(PowerType.Fury, false, uint.MaxValue - 1, uint.MaxValue, uint.MaxValue, uint.MaxValue)] // without overflowing
    [InlineData(PowerType.Fury, false, 10u, 100u, 0u, 10u)]                                        // a gain of zero
    [InlineData(PowerType.Mana, false, 120u, 100u, 8u, 120u)]                                      // above the maximum: as it is
    [InlineData(PowerType.Fury, true, 10u, 100u, 8u, 10u)]                                         // the dead gain nothing
    [InlineData(PowerType.None, false, 10u, 100u, 8u, 10u)]                                        // a pool no cast spends
    [InlineData((PowerType)99, false, 10u, 100u, 8u, 10u)]
    public void Gain_power_only_into_a_living_spendable_pool_up_to_its_maximum(PowerType pool, bool dead, uint current,
        uint max, uint amount, uint expected)
    {
        Assert.Equal(expected, PowerPool.Gain(pool, dead, current, max, amount));
    }

    [Fact]
    public void Empty_only_fury_on_a_reset()
    {
        Assert.True(PowerPool.EmptiesOnReset(PowerType.Fury));
        Assert.False(PowerPool.EmptiesOnReset(PowerType.Mana));
        Assert.False(PowerPool.EmptiesOnReset(PowerType.Energy));
    }
}
