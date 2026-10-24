using Avalon.Common.Mathematics;
using Xunit;

namespace Avalon.Shared.UnitTests.Mathematics;

public class WeightedPickShould
{
    [Theory]
    [InlineData(new[] { 1000, 800 }, 0.5555, 0)]
    [InlineData(new[] { 1000, 800 }, 0.5556, 1)]
    [InlineData(new[] { 1000, 800, 600, 400, 200 }, 0.9333, 3)]
    [InlineData(new[] { 1000, 800, 600, 400, 200 }, 0.9334, 4)]
    [InlineData(new[] { 0, 800 }, 0.0, 1)]
    [InlineData(new[] { 0, 0 }, 0.5, -1)]
    public void Pick_an_index_by_weight(int[] weights, double draw, int index) =>
        Assert.Equal(index, WeightedPick.Index(weights, draw));
}
