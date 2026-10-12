using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Generic;

/// <summary>
/// The server's time-sync ping: about every ten seconds on a world connection, and once right after its handshake. The
/// client answers each one, once, with a <see cref="CPongPacket" />.
/// </summary>
[ProtoContract]
public class SPingPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_PING;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;

    /// <summary>
    /// Server time, in UTC ticks, as this ping was written. It also names the ping: the pong echoes it unchanged, and
    /// the server times the round trip from it.
    /// </summary>
    [ProtoMember(1)] public long ServerTimestamp { get; set; }

    /// <summary>The client receive time of the last pong the server accepted, as the client sent it; 0 before the first.</summary>
    [ProtoMember(2)] public long ClientTimestamp { get; set; }

    /// <summary>
    /// The last round trip in ticks, measured by the server alone: from a ping's <see cref="ServerTimestamp" /> to the
    /// arrival of its pong. It includes the time the client took to answer. 0 before the first accepted pong.
    /// </summary>
    [ProtoMember(3)] public long Rtt { get; set; }

    /// <summary>
    /// Server time minus client time, in ticks, from that same exchange: the ping's <see cref="ServerTimestamp" /> plus
    /// half of <see cref="Rtt" />, minus the pong's <see cref="CPongPacket.ClientReceivedTimestamp" />. 0 before the first
    /// accepted pong.
    /// </summary>
    [ProtoMember(4)] public long Offset { get; set; }

    public static OutboundPacket Create(long serverTicks, long clientTicks, long rtt, long offset, PacketEncoder encoder)
    {
        SPingPacket message = PacketEncoder.Scratch<SPingPacket>();
        message.ServerTimestamp = serverTicks;
        message.ClientTimestamp = clientTicks;
        message.Rtt = rtt;
        message.Offset = offset;
        return encoder.Encode(message, PacketType, NetworkPacketFlags.None, Protocol);
    }
}
