namespace Avalon.Domain.World;

/// <summary>
/// Defines a directional connection between two maps.
/// The server validates that the player is within <see cref="Radius"/> of the portal position
/// before allowing a map transition.
/// </summary>
public class MapPortal
{
    public int Id { get; set; }

    /// <summary>Map that contains this portal (the "from" side).</summary>
    public ushort SourceMapId { get; set; }

    /// <summary>Map the portal leads to (the "to" side).</summary>
    public ushort TargetMapId { get; set; }

    /// <summary>World-space X coordinate of the portal centre.</summary>
    public float X { get; set; }

    /// <summary>World-space Y coordinate of the portal centre.</summary>
    public float Y { get; set; }

    /// <summary>World-space Z coordinate of the portal centre.</summary>
    public float Z { get; set; }

    /// <summary>
    /// Proximity radius used for server-side validation.
    /// The player must be within this distance of (X, Y, Z) to use the portal.
    /// </summary>
    public float Radius { get; set; }
}
