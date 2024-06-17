using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class CWorldSelectPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_WORLD_SELECT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public int WorldId { get; set; }

    public static NetworkPacket Create(int worldId, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();
        
        var worldSelectPacket = new CWorldSelectPacket()
        {
            WorldId = worldId
        };
        
        Serializer.Serialize(memoryStream, worldSelectPacket);
        
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
