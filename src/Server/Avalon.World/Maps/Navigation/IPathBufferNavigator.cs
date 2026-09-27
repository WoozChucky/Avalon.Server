using Avalon.Common.Mathematics;

namespace Avalon.World.Maps.Navigation;

/// <summary>
/// A navigator that can write a route into a list the caller owns (#638), so a caller that re-paths
/// often, such as <see cref="Creatures.Locomotion.WaypointLocomotion" />, refills one buffer instead of
/// receiving a new list each time. World-side on purpose: Avalon.World.Public, the modding API, keeps
/// only <see cref="Public.Maps.IMapNavigator.FindPath" />.
/// </summary>
public interface IPathBufferNavigator
{
    /// <summary>
    /// Clears <paramref name="path" /> and fills it with exactly the points
    /// <see cref="Public.Maps.IMapNavigator.FindPath" /> would return for the same arguments, leaving it
    /// empty when there is no route.
    /// </summary>
    void FindPath(Vector3 start, Vector3 end, List<Vector3> path);
}
