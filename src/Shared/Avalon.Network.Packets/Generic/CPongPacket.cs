using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Generic;

[ProtoContract]
public class CPongPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_PONG;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    
    [ProtoMember(1)] public long LastServerTimestamp { get; set; }
    [ProtoMember(2)] public long ClientReceivedTimestamp { get; set; }
    [ProtoMember(3)] public long ClientSentTimestamp { get; set; }
    
    public static NetworkPacket Create(long lastServerTicks, long clientReceivedTicks)
    {
        using var memoryStream = new MemoryStream();
        
        var p = new CPongPacket
        {
            LastServerTimestamp = lastServerTicks,
            ClientReceivedTimestamp = clientReceivedTicks,
            ClientSentTimestamp = DateTime.UtcNow.Ticks
        };
        
        Serializer.Serialize(memoryStream, p);
        
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
