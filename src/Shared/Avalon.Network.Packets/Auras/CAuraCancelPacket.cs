using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Auras;

/// <summary>
/// Client to server (auras): end one helpful aura on the sender's own character. With <see cref="InstanceKey" /> only
/// the copy with that key ends; without it (a client that does not send one) every copy of the aura ends. Answered
/// with exactly one SAuraCancelResultPacket.
/// </summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_AURA_CANCEL)]
public class CAuraCancelPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_AURA_CANCEL;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public uint AuraId { get; set; }

    /// <summary>
    /// The copy to end, as <see cref="AuraEntryDto.InstanceKey" /> named it; absent ends every copy of
    /// <see cref="AuraId" />.
    /// </summary>
    [ProtoMember(2)] public uint? InstanceKey { get; set; }
}
