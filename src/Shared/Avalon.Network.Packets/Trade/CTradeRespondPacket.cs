using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Trade;

/// <summary>Accepts or declines the trade request the sender holds. Answered with SMSG_TRADE_RESULT.</summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_TRADE_RESPOND)]
public class CTradeRespondPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_TRADE_RESPOND;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public bool Accept { get; set; }
}
