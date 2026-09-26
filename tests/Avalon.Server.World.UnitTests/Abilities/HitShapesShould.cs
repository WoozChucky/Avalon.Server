using Avalon.Common.Mathematics;
using Avalon.World.Abilities.Targeting;
using Xunit;

namespace Avalon.Server.World.UnitTests.Abilities;

/// <summary>
/// Every shape test is 2D on X/Z, and a unit counts when its body circle overlaps the shape, edges
/// included (#164).
/// </summary>
public class HitShapesShould
{
    private static readonly Vector3 Origin = Vector3.zero;
    private static readonly Vector3 AlongZ = new(0f, 0f, 1f);

    private static Vector3 At(float x, float z, float y = 0f) => new(x, y, z);

    [Theory]
    [InlineData(3.5f, true)]    // centre 3.5 m away, circle 3 + body 0.5: touching counts
    [InlineData(3.51f, false)]
    [InlineData(0f, true)]
    public void Count_a_body_touching_the_circle_edge(float distance, bool expected) =>
        Assert.Equal(expected, HitShapes.CircleOverlaps(Origin, 3f, At(distance, 0f), 0.5f));

    [Fact]
    public void Ignore_height_in_every_shape()
    {
        Assert.True(HitShapes.CircleOverlaps(Origin, 1f, At(0f, 0f, y: 50f), 0.5f));
        Assert.True(HitShapes.ConeOverlaps(Origin, AlongZ, 3f, 90f, At(0f, 2f, y: -50f), 0.5f));
        Assert.True(HitShapes.SegmentOverlaps(Origin, At(0f, 10f), At(0f, 5f, y: 99f), 0.5f));
    }

    [Theory]
    [InlineData(0f, 2f, true)]      // straight ahead
    [InlineData(0f, -2f, false)]    // behind
    [InlineData(0f, 3.5f, true)]    // at reach + body
    [InlineData(0f, 3.6f, false)]
    [InlineData(0.2f, 0f, true)]    // body over the apex counts in every direction
    public void Hit_inside_a_90_degree_cone_and_nothing_behind_it(float x, float z, bool expected) =>
        Assert.Equal(expected, HitShapes.ConeOverlaps(Origin, AlongZ, 3f, 90f, At(x, z), 0.5f));

    [Fact]
    public void Count_a_unit_exactly_on_the_half_arc()
    {
        // 45 degrees off +Z, at 2 m: the centre sits on the edge of a 90-degree cone.
        Vector3 onEdge = At(MathF.Sin(MathF.PI / 4f) * 2f, MathF.Cos(MathF.PI / 4f) * 2f);
        Assert.True(HitShapes.ConeOverlaps(Origin, AlongZ, 3f, 90f, onEdge, 0f));
    }

    [Fact]
    public void Widen_the_angular_test_by_the_body_at_that_distance()
    {
        // 50 degrees off +Z at 2 m. Outside a 90-degree cone by 5 degrees, but a 0.5 m body at 2 m
        // spans asin(0.25) = 14.5 degrees, so its circle crosses the edge.
        float radians = 50f * MathF.PI / 180f;
        Vector3 justOutside = At(MathF.Sin(radians) * 2f, MathF.Cos(radians) * 2f);

        Assert.False(HitShapes.ConeOverlaps(Origin, AlongZ, 3f, 90f, justOutside, 0f));
        Assert.True(HitShapes.ConeOverlaps(Origin, AlongZ, 3f, 90f, justOutside, 0.5f));
    }

    [Fact]
    public void Hit_all_round_with_a_360_degree_cone()
    {
        Assert.True(HitShapes.ConeOverlaps(Origin, AlongZ, 3f, 360f, At(0f, -2f), 0.5f));
        Assert.False(HitShapes.ConeOverlaps(Origin, AlongZ, 3f, 360f, At(0f, -4f), 0.5f));
    }

    /// <summary>A NaN position or reach fails every comparison, so the reach test must refuse it rather than let it through (#164).</summary>
    [Fact]
    public void Miss_a_NaN_position_or_reach_even_in_a_360_degree_cone()
    {
        Assert.False(HitShapes.ConeOverlaps(Origin, AlongZ, 3f, 360f, At(float.NaN, float.NaN), 0.5f));
        Assert.False(HitShapes.ConeOverlaps(Origin, AlongZ, float.NaN, 360f, At(0f, 1f), 0.5f));
    }

    [Theory]
    [InlineData(0.4f, 5f, true)]     // passes within the body
    [InlineData(0.6f, 5f, false)]    // misses by more than the body
    [InlineData(0f, 10.5f, true)]    // just past the end, touching
    [InlineData(0f, 11f, false)]
    [InlineData(0f, -0.5f, true)]    // just behind the start, touching
    public void Sweep_a_segment_against_a_body(float x, float z, bool expected) =>
        Assert.Equal(expected, HitShapes.SegmentOverlaps(Origin, At(0f, 10f), At(x, z), 0.5f));

    [Fact]
    public void Treat_a_zero_length_step_as_a_point()
    {
        Assert.True(HitShapes.SegmentOverlaps(At(1f, 1f), At(1f, 1f), At(1.4f, 1f), 0.5f));
        Assert.False(HitShapes.SegmentOverlaps(At(1f, 1f), At(1f, 1f), At(2f, 1f), 0.5f));
    }
}
