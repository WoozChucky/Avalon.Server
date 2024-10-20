using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
[Packet(HandleOn = ComponentType.Auth, Type = NetworkPacketType.CMSG_AUTH)]
public class CAuthPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_AUTH;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public string Username { get; set; }
    [ProtoMember(2)] public string Password { get; set; }

    public static NetworkPacket Create(string username, string password, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();

        var authPacket = new CAuthPacket()
        {
            Username = username,
            Password = password
        };

        Serializer.Serialize(memoryStream, authPacket);

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
