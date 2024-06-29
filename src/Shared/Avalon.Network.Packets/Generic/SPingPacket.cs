using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Generic;

[ProtoContract]
public class SPingPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_PING;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    
    [ProtoMember(1)] public long ServerTimestamp { get; set; }
    [ProtoMember(2)] public long ClientTimestamp { get; set; }
    [ProtoMember(3)] public long Rtt { get; set; }
    [ProtoMember(4)] public long Offset { get; set; }
    
    public static NetworkPacket Create(long serverTicks, long clientTicks, long rtt, long offset)
    {
        using var memoryStream = new MemoryStream();
        
        var pingPacket = new SPingPacket()
        {
            ServerTimestamp = serverTicks,
            ClientTimestamp = clientTicks,
            Rtt = rtt,
            Offset = offset
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
