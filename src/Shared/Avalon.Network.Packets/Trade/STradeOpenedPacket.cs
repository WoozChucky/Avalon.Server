using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Trade;

/// <summary>
/// A request was accepted: the trade window opens, both sides empty, and the trade's first state follows at once. Sent
/// to both players.
/// </summary>
[ProtoContract]
public class STradeOpenedPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_TRADE_OPENED;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public uint TradeId { get; set; }
    [ProtoMember(2)] public uint PartnerCharacterId { get; set; }
    [ProtoMember(3)] public string PartnerName { get; set; } = string.Empty;

    public static OutboundPacket Create(uint tradeId, uint partnerCharacterId, string partnerName, PacketEncoder encoder)
    {
        STradeOpenedPacket message = PacketEncoder.Scratch<STradeOpenedPacket>();
        message.TradeId = tradeId;
        message.PartnerCharacterId = partnerCharacterId;
        message.PartnerName = partnerName;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
