using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Trade;

/// <summary>Closes the sender's open trade; nothing moves. Answered with SMSG_TRADE_RESULT.</summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_TRADE_CANCEL)]
public class CTradeCancelPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_TRADE_CANCEL;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
}
