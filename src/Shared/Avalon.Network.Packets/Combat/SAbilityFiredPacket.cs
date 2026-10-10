using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.World;
using ProtoBuf;
using NetworkPacketFlags = Avalon.Network.Packets.Abstractions.NetworkPacketFlags;
using NetworkProtocol = Avalon.Network.Packets.Abstractions.NetworkProtocol;

namespace Avalon.Network.Packets.Combat;

/// <summary>
/// A circle or cone skill fired (#164), broadcast to the instance so every client can draw it. A circle
/// carries <see cref="Centre" />, a cone <see cref="Direction" /> (a unit vector on X/Z), and both carry the
/// whole <see cref="Footprint" /> (#648). Its hits arrive as the usual damage packets.
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
    // The wire contract stays as it is: IsRequired would change what is serialized (WireSchemaShould pins it).
#pragma warning disable PBN0022
    [ProtoMember(3)] public Vector3Dto Origin { get; set; } = new();
#pragma warning restore PBN0022

    /// <summary>A cone's direction, a unit vector on X/Z; absent for a circle.</summary>
    [ProtoMember(4)] public Vector3Dto? Direction { get; set; }

    /// <summary>A circle's centre; absent for a cone.</summary>
    [ProtoMember(5)] public Vector3Dto? Centre { get; set; }

    /// <summary>
    /// The cast this belongs to (#648): the id its <c>SUnitStartCastPacket</c> carried, unique within the instance,
    /// so a client clears the telegraph of that cast and no other. 0 from a server before #648.
    /// </summary>
    [ProtoMember(6)] public uint CastId { get; set; }

    /// <summary>
    /// The whole footprint that fired, dimensions included (#648), so a client draws it, an instant ability's
    /// too, without knowing the caster's abilities. For a cast-time cast it is the footprint its start carried.
    /// <see cref="Origin" />, <see cref="Direction" /> and <see cref="Centre" /> repeat its members, for clients
    /// from before #648.
    /// </summary>
    [ProtoMember(7)] public AbilityFootprintDto? Footprint { get; set; }

    public static OutboundPacket Create(ulong caster, uint abilityId, uint castId, AbilityFootprintDto footprint,
        PacketEncoder encoder)
    {
        SAbilityFiredPacket message = PacketEncoder.Scratch<SAbilityFiredPacket>();
        message.CasterGuid = caster;
        message.AbilityId = abilityId;
        message.Origin = footprint.Origin ?? new Vector3Dto();
        message.Direction = footprint.Direction;
        message.Centre = footprint.Centre;
        message.CastId = castId;
        message.Footprint = footprint;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
