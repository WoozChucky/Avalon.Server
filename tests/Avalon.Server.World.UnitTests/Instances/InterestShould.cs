using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Instances;
using Avalon.World.Public.Instances;
using Xunit;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>#593: the rule that decides which objects a client's world-state replication covers.</summary>
public class InterestShould
{
    private static readonly ObjectGuid Watcher = new(ObjectType.Character, 593_001);
    private static readonly ObjectGuid Other = new(ObjectType.Creature, 593_002);
    private static readonly InterestRange Range = new(60f, 10f);

    private static bool Visible(float x, bool tracked, float y = 0f, float z = 0f) =>
        Interest.IsVisible(Watcher, Vector3.zero, Other, new Vector3(x, y, z), tracked, Range);

    [Fact] public void Add_an_object_inside_the_radius() => Assert.True(Visible(59f, tracked: false));
    [Fact] public void Add_an_object_exactly_at_the_radius() => Assert.True(Visible(60f, tracked: false));
    [Fact] public void Not_add_an_object_between_the_radius_and_the_margin() => Assert.False(Visible(65f, tracked: false));
    [Fact] public void Keep_a_tracked_object_between_the_radius_and_the_margin() => Assert.True(Visible(65f, tracked: true));
    [Fact] public void Keep_a_tracked_object_exactly_at_radius_plus_margin() => Assert.True(Visible(70f, tracked: true));
    [Fact] public void Drop_a_tracked_object_beyond_radius_plus_margin() => Assert.False(Visible(70.01f, tracked: true));
    [Fact] public void Ignore_height() => Assert.True(Visible(0f, tracked: false, y: 500f));
    [Fact] public void Measure_on_x_and_z() => Assert.False(Visible(50f, tracked: false, z: 50f)); // ~70.7 m

    [Fact]
    public void Always_show_the_watcher_itself_even_far_or_unplaced()
    {
        Assert.True(Interest.IsVisible(Watcher, Vector3.zero, Watcher, new Vector3(1e6f, 0, 0), false, Range));
        Assert.True(Interest.IsVisible(Watcher, new Vector3(float.NaN, 0, 0), Watcher, new Vector3(float.NaN, 0, 0), false, Range));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Never_show_an_object_at_a_non_finite_position(float x) => Assert.False(Visible(x, tracked: true));

    [Fact]
    public void Show_nothing_else_to_a_watcher_at_a_non_finite_position() =>
        Assert.False(Interest.IsVisible(Watcher, new Vector3(float.NaN, 0, 0), Other, Vector3.zero, true, Range));

    [Fact]
    public void Treat_an_overflowing_distance_as_far() =>
        Assert.False(Interest.IsVisible(Watcher, new Vector3(-3e38f, 0, 0), Other, new Vector3(3e38f, 0, 0), true, Range));

    [Fact]
    public void Remove_at_the_radius_when_the_margin_is_zero() =>
        Assert.False(Interest.IsVisible(Watcher, Vector3.zero, Other, new Vector3(60.01f, 0, 0), true, new InterestRange(60f, 0f)));
}
