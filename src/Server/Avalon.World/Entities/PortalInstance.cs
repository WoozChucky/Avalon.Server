using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public;

namespace Avalon.World.Entities;

public sealed class PortalInstance : IWorldObject
{
    public PortalInstance(ObjectGuid guid, Vector3 position, float radius, ushort targetMapId, byte role)
    {
        Guid = guid;
        Position = position;
        Velocity = Vector3.zero;
        Orientation = Vector3.zero;
        Radius = radius;
        TargetMapId = targetMapId;
        Role = role;
    }

    public ObjectGuid Guid { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Velocity { get; set; }
    public Vector3 Orientation { get; set; }
    public float Radius { get; }
    public ushort TargetMapId { get; }

    /// <summary>0 = Back, 1 = Forward. Mirrors <see cref="Avalon.Domain.World.PortalRole"/>.</summary>
    public byte Role { get; }
}
