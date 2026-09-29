using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
[Packet(HandleOn = ComponentType.Auth, Type = NetworkPacketType.CMSG_AUTH_GAME_TICKET)]
public class CAuthGameTicketPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_AUTH_GAME_TICKET;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public string Ticket { get; set; } = string.Empty;

    public static NetworkPacket Create(string ticket, EncryptFunc encryptFunc) =>
        PacketSerializationHelper.Serialize(new CAuthGameTicketPacket { Ticket = ticket },
            PacketType, Flags, Protocol, encryptFunc);
}
