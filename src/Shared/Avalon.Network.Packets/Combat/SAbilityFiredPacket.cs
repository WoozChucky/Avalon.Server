using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.World;
using ProtoBuf;
using NetworkPacketFlags = Avalon.Network.Packets.Abstractions.NetworkPacketFlags;
using NetworkProtocol = Avalon.Network.Packets.Abstractions.NetworkProtocol;

namespace Avalon.Network.Packets.Combat;

/// <summary>
/// A circle or cone skill fired (#164), broadcast to the instance so every client can draw it. A circle
/// carries <see cref="Centre" />, a cone <see cref="Direction" /> (a unit vector on X/Z). Its hits
/// arrive as the usual damage packets.
/// </summary>
[ProtoContract]
public class SAbilityFiredPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_ABILITY_FIRED;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public ulong CasterGuid { get; set; }
    [ProtoMember(2)] public uint AbilityId { get; set; }

    /// <summary>The caster's position when it fired.</summary>
    [ProtoMember(3)] public Vector3Dto Origin { get; set; } = new();

    /// <summary>A cone's direction, a unit vector on X/Z; absent for a circle.</summary>
    [ProtoMember(4)] public Vector3Dto? Direction { get; set; }

    /// <summary>A circle's centre; absent for a cone.</summary>
    [ProtoMember(5)] public Vector3Dto? Centre { get; set; }

    public static NetworkPacket Create(ulong caster, uint abilityId, Vector3 origin, Vector3? direction, Vector3? centre,
        EncryptFunc encrypt)
        => PacketSerializationHelper.Serialize(
            new SAbilityFiredPacket
            {
                CasterGuid = caster,
                AbilityId = abilityId,
                Origin = Vector3Dto.From(origin),
                Direction = direction is { } d ? Vector3Dto.From(d) : null,
                Centre = centre is { } c ? Vector3Dto.From(c) : null,
            },
            PacketType, Flags, Protocol, encrypt);
}
