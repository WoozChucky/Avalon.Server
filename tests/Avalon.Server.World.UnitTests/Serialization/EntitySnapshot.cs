using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;
using Avalon.World.Public.Enums;

namespace Avalon.Server.World.UnitTests.Serialization;

/// <summary>
/// What a client ends up knowing about one entity after reading one entity-state payload.
/// </summary>
/// <remarks>
/// Every member is nullable so that "did not arrive" is a distinct answer from "arrived
/// carrying zero". That distinction is the whole of what the entity-state format does, so a
/// comparison that could not express it would agree with an encoder that dropped a field and
/// with one that sent a zero instead.
///
/// The members are named after the server properties they carry rather than after the layout
/// they arrive in, because the layouts differ and the information does not.
/// </remarks>
public sealed record EntitySnapshot
{
    public ObjectType Type { get; init; }

    public Vector3? Position { get; init; }
    public Vector3? Velocity { get; init; }

    /// <summary>Yaw alone. The payload carries <c>Orientation.y</c> and discards x and z.</summary>
    public float? Orientation { get; init; }

    public MoveState? MoveState { get; init; }
    public uint? Health { get; init; }
    public uint? CurrentHealth { get; init; }
    public PowerType? PowerType { get; init; }
    public uint? Power { get; init; }
    public uint? CurrentPower { get; init; }
    public ushort? Level { get; init; }
    public bool? IsDead { get; init; }
    public ulong? Experience { get; init; }
    public ulong? RequiredExperience { get; init; }
    public ulong? CreatureMetadataId { get; init; }
    public string? Name { get; init; }

    public float? PortalRadius { get; init; }
    public ushort? PortalTargetMapId { get; init; }
    public byte? PortalRole { get; init; }
}
