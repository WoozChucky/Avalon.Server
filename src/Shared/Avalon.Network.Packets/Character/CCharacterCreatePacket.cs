using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Character;

[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_CHARACTER_CREATE)]
public class CCharacterCreatePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_CHARACTER_CREATE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public string Name { get; set; }
    [ProtoMember(2)] public int Class { get; set; }

    /// <summary>
    /// A <c>CharacterGender</c> value. Optional on the wire: a client that omits it sends 0,
    /// which is <c>CharacterGender.Male</c>. The server rejects any value the enum does not define.
    /// </summary>
    [ProtoMember(3)] public int Gender { get; set; }

    public static NetworkPacket Create(string name, int @class, int gender, EncryptFunc encrypt)
    {
        using var memoryStream = new MemoryStream();

        var p = new CCharacterCreatePacket()
        {
            Name = name,
            Class = @class,
            Gender = gender
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
