using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Handshake;

[ProtoContract]
public class CRequestServerInfoPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_SERVER_INFO;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    
    [ProtoMember(1)] public int ClientVersion { get; set; }

    public static NetworkPacket Create(int clientVersion)
    {
        using var memoryStream = new MemoryStream();
        
        var packet = new CRequestServerInfoPacket()
        {
            ClientVersion = clientVersion
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
