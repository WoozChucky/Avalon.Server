using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_GAME_ADMISSION)]
public sealed class CGameAdmissionPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_GAME_ADMISSION;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.ClearText;
    [ProtoMember(1)] public string JoinTicket { get; set; } = string.Empty;
    [ProtoMember(2)] public byte[] PublicKey { get; set; } = [];
    public static NetworkPacket Create(string joinTicket, byte[] publicKey) => PacketSerializationHelper.SerializeUnencrypted(
        new CGameAdmissionPacket { JoinTicket = joinTicket, PublicKey = publicKey }, PacketType, Flags, Protocol);
}
