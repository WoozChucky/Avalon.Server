using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Generic;

[ProtoContract]
public class SPongPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_PONG;
    public static NetworkProtocol Protocol = NetworkProtocol.Udp;

    [ProtoMember(1)] public long SequenceNumber { get; set; }
    [ProtoMember(2)] public long Ticks { get; set; }

    public static OutboundPacket Create(long sequenceNumber, PacketEncoder encoder, long? ticks = null)
    {
        SPongPacket message = PacketEncoder.Scratch<SPongPacket>();
        message.SequenceNumber = sequenceNumber;
        message.Ticks = ticks ?? DateTime.UtcNow.Ticks;
        return encoder.Encode(message, PacketType, NetworkPacketFlags.None, Protocol);
    }
}
