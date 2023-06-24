using ProtoBuf;

namespace Avalon.Network.Packets.Generic;

[ProtoContract]
public class CPingPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_PING;
    public static NetworkProtocol Protocol = NetworkProtocol.Udp;
    
    [ProtoMember(1)] public long Ticks { get; set; }
    
    public static NetworkPacket Create(long? ticks = null)
    {
        using var memoryStream = new MemoryStream();
        
        var pingPacket = new CPingPacket()
        {
            Ticks = ticks ?? DateTime.UtcNow.Ticks
        };
        
        Serializer.Serialize(memoryStream, pingPacket);
        
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = NetworkPacketFlags.None,
                Protocol = Protocol,
                Version = 0
            },
            Payload = memoryStream.ToArray()
        };
    }
}
