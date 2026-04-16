using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Character;

[ProtoContract]
public class SCharacterDeletedPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHARACTER_DELETED;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public SCharacterDeletedResult Result { get; set; }

    public static NetworkPacket Create(SCharacterDeletedResult result, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SCharacterDeletedPacket { Result = result },
            PacketType, Flags, Protocol, encryptFunc);
}

public enum SCharacterDeletedResult : short
{
    Success = 0,
    InGame = 1,
    InternalError = 2,
    Mail = 3,
    Auction = 4,
}
