using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
[Packet(HandleOn = ComponentType.Auth, Type = NetworkPacketType.CMSG_REGISTER)]
public class CRegisterPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_REGISTER;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public string Username { get; set; }
    [ProtoMember(2)] public string Email { get; set; }
    [ProtoMember(3)] public string Password { get; set; }

    public static NetworkPacket Create(string username, string email, string password, EncryptFunc encryptFunc)
    {
        using var memoryStream = new MemoryStream();

        var p = new CRegisterPacket()
        {
            Username = username,
            Email = email,
            Password = password
        };

        Serializer.Serialize(memoryStream, p);

        var buffer = encryptFunc(memoryStream.ToArray());

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
