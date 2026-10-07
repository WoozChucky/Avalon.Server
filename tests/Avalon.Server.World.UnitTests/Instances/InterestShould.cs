using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Instances;
using Avalon.World.Public.Instances;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// #593: the rule that decides which objects a client's world-state replication covers, at its edges. The
/// watcher's own character, always in view, is pinned through a real MapInstance (InterestReplicationShould).
/// </summary>
public class InterestShould
{
    private static readonly ObjectGuid s_watcher = new(ObjectType.Character, 593_001);
    private static readonly ObjectGuid s_other = new(ObjectType.Creature, 593_002);

    /// <summary>
    /// An object enters within the radius (60 m) and, once tracked, stays until beyond radius + margin, both
    /// inclusive, on X/Z only; a non-finite position, the watcher's or the object's, and an overflowing distance
    /// are never in view.
    /// </summary>
    [Theory]
    [InlineData(0f, 60f, 0f, 0f, false, 10f, true)]                     // exactly at the radius
    [InlineData(0f, 65f, 0f, 0f, false, 10f, false)]                    // in the margin, not yet tracked
    [InlineData(0f, 65f, 0f, 0f, true, 10f, true)]                      // in the margin, tracked
    [InlineData(0f, 70f, 0f, 0f, true, 10f, true)]                      // exactly at radius + margin
    [InlineData(0f, 70.01f, 0f, 0f, true, 10f, false)]                  // just beyond it
    [InlineData(0f, 0f, 500f, 0f, false, 10f, true)]                    // height is ignored
    [InlineData(0f, 50f, 0f, 50f, false, 10f, false)]                   // X and Z together: ~70.7 m
    [InlineData(0f, float.NaN, 0f, 0f, true, 10f, false)]
    [InlineData(0f, float.PositiveInfinity, 0f, 0f, true, 10f, false)]
    [InlineData(float.NaN, 0f, 0f, 0f, true, 10f, false)]               // the watcher's position
    [InlineData(-3e38f, 3e38f, 0f, 0f, true, 10f, false)]               // a distance that overflows
    [InlineData(0f, 60.01f, 0f, 0f, true, 0f, false)]                   // no margin: gone past the radius
    public void Decide_whether_an_object_is_in_view(float watcherX, float x, float y, float z, bool tracked,
        float margin, bool visible) =>
        Assert.Equal(visible, Interest.IsVisible(s_watcher, new Vector3(watcherX, 0f, 0f), s_other,
            new Vector3(x, y, z), tracked, new InterestRange(60f, margin)));
}
