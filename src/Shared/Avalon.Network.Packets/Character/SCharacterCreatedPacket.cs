using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

[ProtoContract]
public class SCharacterCreatedPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHARACTER_CREATED;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public SCharacterCreateResult Result { get; set; }

    public static NetworkPacket Create(SCharacterCreateResult result, Func<byte[], byte[]> encrypt)
    {
        using var memoryStream = new MemoryStream();

        var p = new SCharacterCreatedPacket()
        {
            Result = result
        };

        Serializer.Serialize(memoryStream, p);

        var buffer = encrypt(memoryStream.ToArray());

        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = Flags,
                Protocol = Protocol,
                Version = 0
            },
            Payload = buffer
        };
    }
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
