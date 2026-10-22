using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Trade;

/// <summary>
/// The trade window closes: <see cref="TradeResult.Ok" /> when the trade completed, otherwise why it ended, as the
/// recipient sees it. Nothing moved unless it completed.
/// </summary>
[ProtoContract]
public class STradeClosedPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_TRADE_CLOSED;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public uint TradeId { get; set; }
    [ProtoMember(2)] public TradeResult Result { get; set; }

    public static OutboundPacket Create(uint tradeId, TradeResult result, PacketEncoder encoder)
    {
        STradeClosedPacket message = PacketEncoder.Scratch<STradeClosedPacket>();
        message.TradeId = tradeId;
        message.Result = result;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
