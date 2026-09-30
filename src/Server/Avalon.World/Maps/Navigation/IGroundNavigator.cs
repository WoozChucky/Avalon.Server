using Avalon.Common.Mathematics;

namespace Avalon.World.Maps.Navigation;

/// <summary>
/// A navigator that can say where the navmesh ground is near a point, and whether there is any (#720).
/// <see cref="Public.Maps.IMapNavigator.SampleGroundHeight" /> answers the height alone and hands back the
/// height it was given when it finds nothing, so a caller cannot tell "on the ground already" from
/// "off the mesh". World-side on purpose, like <see cref="IPathBufferNavigator" />: Avalon.World.Public,
/// the modding API, keeps only <see cref="Public.Maps.IMapNavigator.SampleGroundHeight" />.
/// </summary>
public interface IGroundNavigator
{
    /// <summary>
    /// The ground near <paramref name="near" />, searched in the same box
    /// <see cref="Public.Maps.IMapNavigator.SampleGroundHeight" /> uses (<paramref name="near" />'s height
    /// centring it vertically), written to <paramref name="ground" />; what that point is depends on the
    /// kind returned (see <see cref="NavmeshGroundKind" />).
    /// </summary>
    NavmeshGroundKind FindGround(Vector3 near, out Vector3 ground);
}
