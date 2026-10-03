using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Auras;

/// <summary>
/// Client to server (auras): end every copy of one helpful aura on the sender's own character. Answered with exactly
/// one SAuraCancelResultPacket.
/// </summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_AURA_CANCEL)]
public class CAuraCancelPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_AURA_CANCEL;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public uint AuraId { get; set; }
}
