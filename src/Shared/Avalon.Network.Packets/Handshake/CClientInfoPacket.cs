using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Handshake;

[ProtoContract]
public class CClientInfoPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_CLIENT_INFO;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    
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
                Flags = NetworkPacketFlags.ClearText,
                Protocol = Protocol,
                Version = 0
            },
            Payload = buffer.ToArray()
        };
    }
}
