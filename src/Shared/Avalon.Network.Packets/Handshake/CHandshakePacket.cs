using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Handshake;

[ProtoContract]
public class CHandshakePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_CLIENT_HANDSHAKE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;

    [ProtoMember(1)] public byte[] HandshakeData { get; set; }

    public static NetworkPacket Create(byte[] handshakeData, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();
        
        var packet = new CHandshakePacket()
        {
            HandshakeData = handshakeData
        };
        
        Serializer.Serialize(memoryStream, packet);

        var buffer = encryptFunc(memoryStream.ToArray());
        
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = NetworkPacketFlags.Encrypted,
                Protocol = Protocol,
                Version = 0
            },
            Payload = buffer
        };
    }
}
