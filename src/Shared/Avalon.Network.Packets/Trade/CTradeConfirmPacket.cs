using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Trade;

/// <summary>Confirms the trade as it stands at Revision (SMSG_TRADE_STATE's). Answered with SMSG_TRADE_RESULT.</summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_TRADE_CONFIRM)]
public class CTradeConfirmPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_TRADE_CONFIRM;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public uint Revision { get; set; }
}
