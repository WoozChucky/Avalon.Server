using Avalon.Combat;

namespace Avalon.Combat.UnitTests;

/// <summary>A heal never raises health past the maximum, and never lowers health already above it (#548).</summary>
public class HealRulesShould
{
    [Fact]
    public void Add_the_heal_below_the_maximum()
    {
        Assert.Equal(90u, HealRules.After(before: 50, max: 100, amount: 40));
    }

    [Fact]
    public void Stop_at_the_maximum()
    {
        Assert.Equal(100u, HealRules.After(before: 95, max: 100, amount: 40));
        Assert.Equal(uint.MaxValue, HealRules.After(uint.MaxValue - 1, uint.MaxValue, uint.MaxValue));
    }

    [Fact]
    public void Leave_health_at_or_above_the_maximum_as_it_is()
    {
        Assert.Equal(100u, HealRules.After(before: 100, max: 100, amount: 40));
        Assert.Equal(130u, HealRules.After(before: 130, max: 100, amount: 40));
    }
}
