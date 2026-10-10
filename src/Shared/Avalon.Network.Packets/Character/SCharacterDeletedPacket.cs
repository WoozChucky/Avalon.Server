using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

[ProtoContract]
public class SCharacterDeletedPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHARACTER_DELETED;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public SCharacterDeletedResult Result { get; set; }

    public static OutboundPacket Create(SCharacterDeletedResult result, PacketEncoder encoder)
        => encoder.Encode(
            new SCharacterDeletedPacket { Result = result },
            PacketType, Flags, Protocol);
}

public enum SCharacterDeletedResult : short
{
    Success = 0,
    InGame = 1,
    InternalError = 2,
    Mail = 3,
    Auction = 4,
}
