using Avalon.Common.Mathematics;
using Avalon.World.Public.Abilities;
using Xunit;

namespace Avalon.Server.World.UnitTests.Abilities;

public class AbilityAimShould
{
    [Theory]
    [InlineData(0f, 0f, 1f)]
    [InlineData(90f, 1f, 0f)]
    [InlineData(180f, 0f, -1f)]
    public void Turn_a_yaw_into_a_unit_facing_on_X_Z(float yaw, float x, float z)
    {
        Vector3 facing = AbilityAim.FacingFromYaw(yaw);
        Assert.Equal(x, facing.x, 3);
        Assert.Equal(0f, facing.y);
        Assert.Equal(z, facing.z, 3);
    }

    /// <summary>Review focus 1: the client's yaw is written unchecked, so a NaN must not become NaN geometry.</summary>
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void Aim_along_Z_for_a_non_finite_yaw(float yaw)
    {
        Vector3 facing = AbilityAim.FacingFromYaw(yaw);
        Assert.Equal(new Vector3(0f, 0f, 1f), facing);
    }

    [Fact]
    public void Point_toward_the_aim_point_ignoring_height()
    {
        var aim = new AbilityAim(new Vector3(0f, 0f, 1f), new Vector3(3f, 40f, 0f));
        Vector3 direction = aim.DirectionFrom(Vector3.zero);
        Assert.Equal(1f, direction.x, 5);
        Assert.Equal(0f, direction.y);
        Assert.Equal(0f, direction.z, 5);
    }

    [Fact]
    public void Fall_back_to_facing_without_a_point_or_with_a_point_on_the_origin()
    {
        var facing = new Vector3(1f, 0f, 0f);
        Assert.Equal(facing, new AbilityAim(facing, null).DirectionFrom(Vector3.zero));
        Assert.Equal(facing, new AbilityAim(facing, new Vector3(0f, 5f, 0f)).DirectionFrom(Vector3.zero));
    }

    /// <summary>Review focus 2: squaring 1e30 overflows a float, so the length is taken in double.</summary>
    [Fact]
    public void Stay_finite_for_a_far_aim_point()
    {
        var aim = new AbilityAim(new Vector3(0f, 0f, 1f), new Vector3(1e30f, 0f, 1e30f));
        Vector3 direction = aim.DirectionFrom(Vector3.zero);
        Assert.Equal(MathF.Sqrt(0.5f), direction.x, 4);
        Assert.Equal(MathF.Sqrt(0.5f), direction.z, 4);
    }
}
