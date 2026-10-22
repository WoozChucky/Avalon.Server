using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Trade;

/// <summary>Asks a player, by character id, to trade. Answered with SMSG_TRADE_RESULT.</summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_TRADE_REQUEST)]
public class CTradeRequestPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_TRADE_REQUEST;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public uint TargetCharacterId { get; set; }
}
