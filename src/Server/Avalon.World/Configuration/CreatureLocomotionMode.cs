namespace Avalon.World.Configuration;

/// <summary>Which implementation moves creatures. Selected once at startup.</summary>
public enum CreatureLocomotionMode
{
    /// <summary>Walk a navmesh path waypoint by waypoint. No awareness of other agents.</summary>
    Waypoint,

    /// <summary>DotRecast crowd steering, with separation and local avoidance between agents.</summary>
    Crowd,
}
