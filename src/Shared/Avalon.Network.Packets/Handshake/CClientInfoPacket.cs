using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Handshake;

[ProtoContract]
[Packet(HandleOn = ComponentType.Auth, Type = NetworkPacketType.CMSG_CLIENT_INFO)]
public class CClientInfoPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_CLIENT_INFO;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.ClearText;

    [ProtoMember(1)] public byte[]? PublicKey { get; set; }

    public static NetworkPacket Create(byte[] publicKey)
    {
        using var memoryStream = new MemoryStream();

        var packet = new CClientInfoPacket()
        {
            PublicKey = publicKey
        };

        Serializer.Serialize(memoryStream, packet);

        memoryStream.TryGetBuffer(out var buffer);

        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = Flags,
                Protocol = Protocol,
                Version = 0
            },
            Payload = buffer.ToArray()
        };
    }
}
