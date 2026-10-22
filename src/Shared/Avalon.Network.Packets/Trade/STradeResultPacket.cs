using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Trade;

/// <summary>
/// The answer to one trade request from this player; also sent unasked to a sender whose request was declined or
/// expired, and to a target whose request expired. Name is the other player the answer is about, when known.
/// </summary>
[ProtoContract]
public class STradeResultPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_TRADE_RESULT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public TradeResult Result { get; set; }
    [ProtoMember(2)] public string? Name { get; set; }

    public static OutboundPacket Create(TradeResult result, string? name, PacketEncoder encoder)
    {
        STradeResultPacket message = PacketEncoder.Scratch<STradeResultPacket>();
        message.Result = result;
        message.Name = name;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
