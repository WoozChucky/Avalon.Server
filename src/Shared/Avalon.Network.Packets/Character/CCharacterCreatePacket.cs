using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_CHARACTER_CREATE)]
public class CCharacterCreatePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_CHARACTER_CREATE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public string Name { get; set; }
    /// <summary>
    /// The class to create. Any number that fits a byte decodes, so the server refuses one that is not a class a
    /// character can have (None included) as InvalidClass; one past 255 fails the packet's decode.
    /// </summary>
    [ProtoMember(2)] public CharacterClass Class { get; set; }

    /// <summary>
    /// Optional on the wire: a client that omits it sends 0, which is <see cref="CharacterGender.Male" />. Any number
    /// that fits a byte decodes, so the server refuses one the enum does not define as InvalidClass; one past 255
    /// fails the packet's decode.
    /// </summary>
    [ProtoMember(3)] public CharacterGender Gender { get; set; }

    public static NetworkPacket Create(string name, CharacterClass @class, CharacterGender gender, EncryptFunc encrypt)
    {
        using var memoryStream = new MemoryStream();

        var p = new CCharacterCreatePacket()
        {
            Name = name,
            Class = @class,
            Gender = gender
        };

        Serializer.Serialize(memoryStream, p);

        byte[] buffer = encrypt(memoryStream.ToArray());

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
