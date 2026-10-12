using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Generic;

/// <summary>
/// The client's answer to one <see cref="SPingPacket" />. A pong that answers no ping the server has outstanding, or
/// answers one a second time, is ignored.
/// </summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_PONG)]
public class CPongPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_PONG;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;

    /// <summary>
    /// The <see cref="SPingPacket.ServerTimestamp" /> of the ping this answers, echoed exactly. The server uses it only
    /// to find that ping (#892); it measures the round trip itself.
    /// </summary>
    [ProtoMember(1)] public long LastServerTimestamp { get; set; }

    /// <summary>
    /// When the client received the ping, in UTC ticks of its own clock. The server reads the client's clock offset
    /// from it, and nothing else.
    /// </summary>
    [ProtoMember(2)] public long ClientReceivedTimestamp { get; set; }

    /// <summary>When the client sent this pong, in UTC ticks of its own clock. The server no longer reads it (#892).</summary>
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
