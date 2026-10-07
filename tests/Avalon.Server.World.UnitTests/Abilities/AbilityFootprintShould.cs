using Avalon.Combat;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abilities;
using Avalon.World.Abilities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Maps;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Abilities;

/// <summary>
/// #648: where an ability lands, resolved once from its row, its aim and the caster's position at cast start.
/// The shape scripts fire with this, and the start broadcast carries it, so it is pinned here once.
/// </summary>
public class AbilityFootprintShould
{
    private static readonly Vector3 s_origin = new(2f, 1f, 3f);
    private readonly IMapNavigator _navigator = Substitute.For<IMapNavigator>();

    public AbilityFootprintShould() =>
        _navigator.RaycastWalkable(default, default).ReturnsForAnyArgs(ci => ci.ArgAt<Vector3>(1));

    private static AbilityMetadata Meta(Avalon.Domain.World.AbilityTemplate template) =>
        AbilityMetadataMapper.From(template);

    private AbilityFootprint Resolve(Avalon.Domain.World.AbilityTemplate template, AbilityAim aim) =>
        AbilityFootprint.Resolve(Meta(template), aim, s_origin, _navigator)!.Value;

    [Fact]
    public void Centre_a_caster_anchored_circle_on_the_origin_with_its_radius()
    {
        AbilityFootprint footprint = Resolve(AbilityTestData.Circle(1, radius: 4f), new AbilityAim(new Vector3(0f, 0f, 1f), null));

        Assert.Equal(AbilityShape.Circle, footprint.Shape);
        Assert.Equal(s_origin, footprint.Origin);
        Assert.Equal(s_origin, footprint.Centre);
        Assert.Null(footprint.Direction);
        Assert.Equal((4f, 0f, 0f), (footprint.Radius, footprint.Reach, footprint.ArcDegrees));
    }

    [Fact]
    public void Clamp_an_aimed_circle_to_its_reach_from_the_origin()
    {
        AbilityFootprint footprint = Resolve(AbilityTestData.AimedCircle(1, reach: 5f, radius: 2f),
            new AbilityAim(new Vector3(0f, 0f, 1f), new Vector3(2f, 0f, 13f)));

        Assert.Equal(2f, footprint.Centre!.Value.x, 4);
        Assert.Equal(8f, footprint.Centre!.Value.z, 4);   // 5 m from the origin, not 10
        Assert.Equal(2f, footprint.Radius);
    }

    [Fact]
    public void Pull_an_aimed_circle_back_where_the_walkable_ray_stops()
    {
        var wall = new Vector3(2f, 1f, 5f);
        _navigator.RaycastWalkable(s_origin, Arg.Any<Vector3>()).Returns(wall);

        AbilityFootprint footprint = Resolve(AbilityTestData.AimedCircle(1, reach: 15f),
            new AbilityAim(new Vector3(0f, 0f, 1f), new Vector3(2f, 0f, 13f)));

        Assert.Equal(wall, footprint.Centre);
    }

    [Fact]
    public void Point_a_movement_cone_along_the_facing_captured_at_cast_start()
    {
        var facing = new Vector3(1f, 0f, 0f);

        AbilityFootprint footprint = Resolve(AbilityTestData.Cone(1, reach: 4f, arc: 60f),
            new AbilityAim(facing, new Vector3(2f, 0f, 30f)));

        Assert.Equal(AbilityShape.Cone, footprint.Shape);
        Assert.Equal(facing, footprint.Direction);
        Assert.Null(footprint.Centre);
        Assert.Equal((0f, 4f, 60f), (footprint.Radius, footprint.Reach, footprint.ArcDegrees));
    }

    [Fact]
    public void Point_a_cursor_cone_from_the_origin_toward_the_aim_point()
    {
        AbilityFootprint footprint = Resolve(AbilityTestData.Cone(1, aim: AbilityAimMode.Cursor),
            new AbilityAim(new Vector3(1f, 0f, 0f), new Vector3(2f, 0f, 13f)));

        Assert.Equal(new Vector3(0f, 0f, 1f), footprint.Direction);
    }

    [Fact]
    public void Lay_a_projectile_lane_toward_the_aim_point_for_its_reach()
    {
        AbilityFootprint footprint = Resolve(AbilityTestData.Projectile(1, reach: 12f),
            new AbilityAim(new Vector3(1f, 0f, 0f), new Vector3(2f, 0f, 4f)));

        Assert.Equal(AbilityShape.Projectile, footprint.Shape);
        Assert.Equal(new Vector3(0f, 0f, 1f), footprint.Direction);
        Assert.Equal(12f, footprint.Reach, 4);   // flies its whole reach, past the point it was aimed at
        Assert.Null(footprint.Centre);
    }

    [Fact]
    public void Cut_a_projectile_lane_where_the_walkable_ray_stops()
    {
        _navigator.RaycastWalkable(s_origin, Arg.Any<Vector3>()).Returns(new Vector3(2f, 1f, 9f));

        AbilityFootprint footprint = Resolve(AbilityTestData.Projectile(1, reach: 12f),
            new AbilityAim(new Vector3(1f, 0f, 0f), new Vector3(2f, 0f, 4f)));

        Assert.Equal(6f, footprint.Reach, 4);
    }
}
