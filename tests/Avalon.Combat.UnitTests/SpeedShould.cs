namespace Avalon.Combat.UnitTests;

/// <summary>#627, #1178: a speed divides a time by 1 + speed / 100; none, or a negative value, leaves it as it is.</summary>
public class SpeedShould
{
    [Fact]
    public void Divide_a_time_by_one_plus_the_speed_over_a_hundred()
    {
        Assert.Equal(0.8 / 1.03, Speed.Scale(TimeSpan.FromSeconds(0.8), 3f).TotalSeconds, precision: 6);
        Assert.Equal(0.8f / 1.03f, Speed.Scale(0.8f, 3f), precision: 6);
        Assert.Equal(2.25f / 1.5f, Speed.Scale(2.25f, 50f), precision: 6);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-10f)]
    [InlineData(float.NaN)]
    [InlineData(float.NegativeInfinity)]
    public void Leave_a_time_as_it_is_with_no_negative_or_unreadable_speed(float speed)
    {
        Assert.Equal(TimeSpan.FromSeconds(0.8), Speed.Scale(TimeSpan.FromSeconds(0.8), speed));
        Assert.Equal(0.8f, Speed.Scale(0.8f, speed));
    }
}
