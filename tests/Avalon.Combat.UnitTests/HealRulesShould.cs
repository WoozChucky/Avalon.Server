namespace Avalon.Combat.UnitTests;

/// <summary>A heal never raises health past the maximum, and never lowers health already above it (#548).</summary>
public class HealRulesShould
{
    [Theory]
    [InlineData(50u, 100u, 40u, 90u)]                                         // below the maximum: added
    [InlineData(95u, 100u, 40u, 100u)]                                        // stops at the maximum
    [InlineData(uint.MaxValue - 1, uint.MaxValue, uint.MaxValue, uint.MaxValue)] // without overflowing
    [InlineData(100u, 100u, 40u, 100u)]                                       // at the maximum: as it is
    [InlineData(130u, 100u, 40u, 130u)]                                       // above it: as it is
    public void Heal_up_to_the_maximum_and_never_lower_health(uint before, uint max, uint amount, uint after)
    {
        Assert.Equal(after, HealRules.After(before, max, amount));
    }
}
