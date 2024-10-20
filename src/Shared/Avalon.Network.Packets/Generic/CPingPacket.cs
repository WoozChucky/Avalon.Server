using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Generic;

[ProtoContract]
public class CPingPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_PING;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;

    [ProtoMember(1)] public long SequenceNumber { get; set; }
    [ProtoMember(2)] public long Ticks { get; set; }

    public static NetworkPacket Create(long sequenceNumber, long? ticks = null)
    {
        using var memoryStream = new MemoryStream();

        var pingPacket = new CPingPacket()
        {
            SequenceNumber = sequenceNumber,
            Ticks = ticks ?? DateTime.UtcNow.Ticks
        };

        Serializer.Serialize(memoryStream, pingPacket);

        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = NetworkPacketFlags.ClearText,
                Protocol = Protocol,
                Version = 0
            },
            Payload = memoryStream.ToArray()
        };
    }
}
