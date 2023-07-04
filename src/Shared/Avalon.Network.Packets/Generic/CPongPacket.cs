using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Generic;

[ProtoContract]
public class CPongPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_PONG;
    public static NetworkProtocol Protocol = NetworkProtocol.Udp;
    
    [ProtoMember(1)] public long SequenceNumber { get; set; }
    [ProtoMember(2)] public int AccountId { get; set; }
    [ProtoMember(3)] public long Ticks { get; set; }
    
    public static NetworkPacket Create(long sequenceNumber, int accountId, long? ticks = null)
    {
        using var memoryStream = new MemoryStream();
        
        var pingPacket = new CPongPacket()
        {
            SequenceNumber = sequenceNumber,
            AccountId = accountId,
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
