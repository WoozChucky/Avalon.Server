using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Handshake;

[ProtoContract]
public class SServerInfoPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_SERVER_INFO;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    
    [ProtoMember(1)] public int ServerVersion { get; set; }
    [ProtoMember(2)] public byte[] PublicKey { get; set; }

    public static NetworkPacket Create(int serverVersion, byte[] publicKey)
    {
        using var memoryStream = new MemoryStream();
        
        var packet = new SServerInfoPacket()
        {
            ServerVersion = serverVersion,
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
