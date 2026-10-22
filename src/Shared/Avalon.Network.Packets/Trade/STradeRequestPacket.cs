using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Trade;

/// <summary>
/// Another player asks this player to trade. Answered with an accept or a decline before it expires; an answer after
/// that is refused with <see cref="TradeResult.NoRequest" />.
/// </summary>
[ProtoContract]
public class STradeRequestPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_TRADE_REQUEST;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    /// <summary>The player asking.</summary>
    [ProtoMember(1)] public uint CharacterId { get; set; }
    [ProtoMember(2)] public string Name { get; set; } = string.Empty;
    [ProtoMember(3)] public CharacterClass Class { get; set; }
    [ProtoMember(4)] public ushort Level { get; set; }

    /// <summary>How long the request stays open.</summary>
    [ProtoMember(5)] public uint ExpiresInMs { get; set; }

    public static OutboundPacket Create(uint characterId, string name, CharacterClass characterClass, ushort level,
        uint expiresInMs, PacketEncoder encoder)
    {
        STradeRequestPacket message = PacketEncoder.Scratch<STradeRequestPacket>();
        message.CharacterId = characterId;
        message.Name = name;
        message.Class = characterClass;
        message.Level = level;
        message.ExpiresInMs = expiresInMs;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
