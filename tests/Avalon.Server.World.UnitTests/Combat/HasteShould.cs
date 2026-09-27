using Avalon.World.Combat;

namespace Avalon.Server.World.UnitTests.Combat;

/// <summary>#627: haste divides a time by 1 + haste / 100; none, or a negative value, leaves it as it is.</summary>
public class HasteShould
{
    [Fact]
    public void Divide_a_time_by_one_plus_the_haste_over_a_hundred()
    {
        Assert.Equal(0.8 / 1.03, Haste.Scale(TimeSpan.FromSeconds(0.8), 3f).TotalSeconds, precision: 6);
        Assert.Equal(0.8f / 1.03f, Haste.Scale(0.8f, 3f), precision: 6);
        Assert.Equal(2.25f / 1.5f, Haste.Scale(2.25f, 50f), precision: 6);
    }

    [Fact]
    public void Change_nothing_at_zero_haste()
    {
        Assert.Equal(TimeSpan.FromSeconds(0.8), Haste.Scale(TimeSpan.FromSeconds(0.8), 0f));
        Assert.Equal(0.8f, Haste.Scale(0.8f, 0f));
    }

    [Theory]
    [InlineData(-10f)]
    [InlineData(float.NaN)]
    [InlineData(float.NegativeInfinity)]
    public void Treat_a_negative_or_unreadable_haste_as_none(float haste)
    {
        Assert.Equal(TimeSpan.FromSeconds(0.8), Haste.Scale(TimeSpan.FromSeconds(0.8), haste));
        Assert.Equal(0.8f, Haste.Scale(0.8f, haste));
    }
}
