namespace Avalon.Combat.UnitTests;

/// <summary>#526, #1181: a mana gain is capped at the maximum and refused to the dead.</summary>
public class PowerPoolShould
{
    [Theory]
    [InlineData(false, 10u, 100u, 8u, 18u)]
    [InlineData(false, 97u, 100u, 8u, 100u)]                                       // capped at the maximum
    [InlineData(false, uint.MaxValue - 1, uint.MaxValue, uint.MaxValue, uint.MaxValue)] // without overflowing
    [InlineData(false, 10u, 100u, 0u, 10u)]                                        // a gain of zero
    [InlineData(false, 120u, 100u, 8u, 120u)]                                      // above the maximum: as it is
    [InlineData(true, 10u, 100u, 8u, 10u)]                                         // the dead gain nothing
    public void Gain_mana_only_while_alive_up_to_the_maximum(bool dead, uint current, uint max, uint amount,
        uint expected)
    {
        Assert.Equal(expected, PowerPool.Gain(dead, current, max, amount));
    }
}
