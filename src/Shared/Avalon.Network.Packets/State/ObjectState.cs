using ProtoBuf;

namespace Avalon.Network.Packets.State;

/// <summary>
///     A position, a velocity or any other triple of coordinates.
/// </summary>
/// <remarks>
///     A message rather than three members on the parent, so that a triple is either sent or
///     not sent as a whole. Three separate members would have made "the entity has not moved"
///     and "the entity is at the origin" the same bytes.
/// </remarks>
[ProtoContract]
public class Vec3
{
    [ProtoMember(1)] public float X { get; set; }
    [ProtoMember(2)] public float Y { get; set; }
    [ProtoMember(3)] public float Z { get; set; }
}

/// <summary>
///     Everything one entity replication message can say about one entity.
/// </summary>
/// <remarks>
///     Every member but the identifier is optional, and that is the whole design: a member is
///     present when the server has something to say about it and absent otherwise, which is
///     what makes one message enough for every kind of entity. A character sets the experience
///     members and a portal does not; a portal sets the three portal members and nothing else
///     does; a projectile sets three members and leaves the other fifteen out. None of them
///     pays for the members it does not set.
///
///     This replaced five separate layouts whose shape was decided by the type byte of the
///     guid together with whether the message was an add or an update — neither of which was
///     in the payload, so a reader had to know both before it could tell what the first byte
///     was. Presence removes the question: a reader parses the same message either way and
///     asks which members arrived. The guid still says what kind of entity this is, because
///     gameplay needs to know, but it is no longer needed to parse.
///
///     Absent is not the same as zero and the distinction carries meaning throughout. A
///     character broadcast to its own player omits position, velocity and orientation because
///     they travel on another packet, and omitting them is different from claiming the
///     character is at the origin and stationary.
/// </remarks>
[ProtoContract]
public class ObjectState
{
    [ProtoMember(1)] public ulong Guid { get; set; }

    [ProtoMember(2)] public Vec3? Position { get; set; }
    [ProtoMember(3)] public Vec3? Velocity { get; set; }

    /// <summary>Yaw. Only rotation about the vertical axis is replicated.</summary>
    [ProtoMember(4)] public float? Orientation { get; set; }

    [ProtoMember(5)] public MoveState? MoveState { get; set; }

    /// <summary>Maximum health. <see cref="CurrentHealth" /> is the live value.</summary>
    [ProtoMember(6)] public uint? Health { get; set; }

    [ProtoMember(7)] public uint? CurrentHealth { get; set; }

    [ProtoMember(8)] public PowerType? PowerType { get; set; }

    /// <summary>Maximum power. Absent for a unit whose power type is None.</summary>
    [ProtoMember(9)] public uint? Power { get; set; }

    [ProtoMember(10)] public uint? CurrentPower { get; set; }

    [ProtoMember(11)] public ushort? Level { get; set; }

    [ProtoMember(12)] public bool? IsDead { get; set; }

    [ProtoMember(13)] public ulong? Experience { get; set; }
    [ProtoMember(14)] public ulong? RequiredExperience { get; set; }

    /// <summary>The template a creature was spawned from. Only creatures carry one.</summary>
    [ProtoMember(15)] public ulong? CreatureMetadataId { get; set; }

    [ProtoMember(16)] public string? Name { get; set; }

    /// <summary>How close a character must be for a portal to take them.</summary>
    [ProtoMember(17)] public float? PortalRadius { get; set; }

    [ProtoMember(18)] public ushort? PortalTargetMapId { get; set; }

    /// <summary>0 = back, 1 = forward.</summary>
    [ProtoMember(19)] public byte? PortalRole { get; set; }
}
