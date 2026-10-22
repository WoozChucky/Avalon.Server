using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Trade;

/// <summary>Offers the whole stack in a Bag slot into a free trade slot. Answered with SMSG_TRADE_RESULT.</summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_TRADE_OFFER_ITEM)]
public class CTradeOfferItemPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_TRADE_OFFER_ITEM;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public byte BagSlot { get; set; }
    [ProtoMember(2)] public byte TradeSlot { get; set; }
}
