using Avalon.World.Instances;
using Xunit;

namespace Avalon.Server.World.UnitTests.Instances;

public class ExperienceAwardShould
{
    [Theory]
    [InlineData(3, 1, 5, 1.0)]      // inside the band
    [InlineData(1, 1, 5, 1.0)]      // on the lower edge
    [InlineData(5, 1, 5, 1.0)]      // on the upper edge
    [InlineData(6, 1, 5, 0.75)]     // one over
    [InlineData(10, 1, 5, 0.2373)]  // five over
    [InlineData(14, 1, 5, 0.0751)]  // nine over
    [InlineData(1, 2, 5, 0.75)]     // one under, symmetric
    public void Scale_Experience_By_Distance_Outside_The_Band(
        int playerLevel, int bandMin, int bandMax, double expected)
    {
        double actual = MapInstance.BandScale((ushort)playerLevel, (ushort)bandMin, (ushort)bandMax, 0.75f);

        Assert.Equal(expected, actual, precision: 3);
    }

    /// <summary>
    /// <c>MapTemplate.MinLevel</c> and <c>MaxLevel</c> are both nullable. A map with no band scales
    /// nothing — it must not be read as a band of 0-0, which would wipe out every award on that map.
    /// </summary>
    [Fact]
    public void Not_Scale_At_All_When_The_Map_Has_No_Band()
    {
        Assert.Equal(1.0, MapInstance.BandScale(40, null, null, 0.75f), precision: 3);
        Assert.Equal(1.0, MapInstance.BandScale(40, 1, null, 0.75f), precision: 3);
        Assert.Equal(1.0, MapInstance.BandScale(40, null, 5, 0.75f), precision: 3);
    }

    [Fact]
    public void Never_Return_A_Negative_Scale_However_Far_Out_The_Player_Is()
    {
        double scale = MapInstance.BandScale(60000, 1, 5, 0.75f);

        Assert.True(scale >= 0.0, $"scale went negative at {scale}");
        Assert.True(scale < 0.001, "an absurd level difference should award essentially nothing");
    }
}
