using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

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

/// <summary>The answer to a character create. Append-only: the values cross the wire.</summary>
public enum SCharacterCreateResult
{
    Success,
    NameAlreadyExists,
    NameTooShort,
    NameTooLong,
    InvalidClass,
    MaxCharactersReached,
    AlreadyInGame,
    InternalDatabaseError,

    /// <summary>The name is not 3 to 12 ASCII letters (#757); too short and too long keep their own answers.</summary>
    NameInvalid
}
