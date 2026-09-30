using Avalon.Balance.Config;
using Xunit;

namespace Avalon.Balance.UnitTests;

public class BandShould
{
    [Theory]
    [InlineData(10d, 0d)]
    [InlineData(15d, 0d)]
    [InlineData(20d, 0d)]
    [InlineData(8d, 2d)]
    [InlineData(23d, 3d)]
    public void Measure_the_distance_outside_the_band(double value, double distance) =>
        Assert.Equal(distance, new Band { Min = 10, Max = 20 }.Distance(value));

    [Theory]
    [InlineData(10d, Grade.Green)]
    [InlineData(15d, Grade.Green)]
    [InlineData(20d, Grade.Green)]
    public void Grade_a_value_inside_the_band_green(double value, Grade grade) =>
        Assert.Equal(grade, new Band { Min = 10, Max = 20 }.Grade(value, 15));

    [Theory]
    [InlineData(1000d, Grade.Green)]
    [InlineData(80d, Grade.Green)]
    [InlineData(70d, Grade.Yellow)]   // 10 below 80, within 15 % of 80 (12)
    [InlineData(60d, Grade.Red)]      // 20 below, beyond 12
    public void Grade_against_a_band_with_only_a_minimum(double value, Grade grade) =>
        Assert.Equal(grade, new Band { Min = 80 }.Grade(value, 15));

    [Theory]
    [InlineData(-5d, Grade.Green)]
    [InlineData(40d, Grade.Green)]
    [InlineData(46d, Grade.Yellow)]   // 6 above 40, within 15 % of 40 (6)
    [InlineData(47d, Grade.Red)]
    public void Grade_against_a_band_with_only_a_maximum(double value, Grade grade) =>
        Assert.Equal(grade, new Band { Max = 40 }.Grade(value, 15));

    [Theory]
    [InlineData(100d, Grade.Green)]
    [InlineData(99d, Grade.Yellow)]   // 1 below, within 15 % of 100
    [InlineData(80d, Grade.Red)]      // 20 below, beyond 15
    [InlineData(101d, Grade.Yellow)]
    public void Grade_against_a_band_whose_ends_are_equal(double value, Grade grade) =>
        Assert.Equal(grade, new Band { Min = 100, Max = 100 }.Grade(value, 15));

    // 25 % of 8 and of 16 are exact in binary floating point, so the limit itself is tested, not a rounding of it.
    [Theory]
    [InlineData(6d, Grade.Yellow)]       // exactly 2 below the minimum
    [InlineData(5.999d, Grade.Red)]      // just beyond
    [InlineData(20d, Grade.Yellow)]      // exactly 4 above the maximum, 25 % of 16
    [InlineData(20.001d, Grade.Red)]
    public void Grade_a_value_at_the_tolerance_limit_yellow_and_one_beyond_it_red(double value, Grade grade) =>
        Assert.Equal(grade, new Band { Min = 8, Max = 16 }.Grade(value, 25));

    [Theory]
    [InlineData(0d, Grade.Green)]
    [InlineData(0.25d, Grade.Yellow)]   // an edge of 0 counts as 1: 25 % of 1
    [InlineData(0.26d, Grade.Red)]
    public void Give_an_edge_of_zero_a_yellow_zone_of_the_tolerance_of_one(double value, Grade grade) =>
        Assert.Equal(grade, new Band { Max = 0 }.Grade(value, 25));

    [Fact]
    public void Grade_no_value_red()
    {
        var band = new Band { Min = 0, Max = 100 };

        Assert.Equal(Grade.Red, band.Grade(null, 15));
        Assert.Equal(Grade.Red, band.Grade(double.NaN, 15));
    }

    [Fact]
    public void Describe_each_shape_of_band()
    {
        Assert.Equal("100%", new Band { Min = 100, Max = 100 }.Describe("%"));
        Assert.Equal("4-8s", new Band { Min = 4, Max = 8 }.Describe("s"));
        Assert.Equal(">= 80%", new Band { Min = 80 }.Describe("%"));
        Assert.Equal("<= 2.5s", new Band { Max = 2.5 }.Describe("s"));
        Assert.Equal("any", new Band().Describe("%"));
    }
}
