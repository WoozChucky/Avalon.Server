using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Character;

[ProtoContract]
public class SCharacterCreatedPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHARACTER_CREATED;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public SCharacterCreateResult Result { get; set; }

    public static NetworkPacket Create(SCharacterCreateResult result, EncryptFunc encrypt)
        => PacketSerializationHelper.Serialize(
            new SCharacterCreatedPacket { Result = result },
            PacketType, Flags, Protocol, encrypt);
}

public enum SCharacterCreateResult
{
    Success,
    NameAlreadyExists,
    NameTooShort,
    NameTooLong,
    InvalidClass,
    MaxCharactersReached,
    AlreadyInGame,
    InternalDatabaseError
}
