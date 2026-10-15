using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Generic;

/// <summary>
/// The server's time-sync ping: about every ten seconds on a world connection, and once right after its handshake. The
/// client answers each one, once, with a <see cref="CPongPacket" />, and sends no other pong.
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

    /// <summary>
    /// The world's mean tick time, in microseconds (#1016): the mean of the busy time of the world's whole ticks (from a
    /// tick's start to its end, every instance and every connection in it, without the wait for the next tick; what
    /// <c>world.tick.duration</c> times) that ended in the last completed window of about one second. The same figure for
    /// every connection of the world, measured always, not only under tracing, and a window old at most when the ping is
    /// queued. One frame at 60 Hz is 16,667. 0 means not reported: no window has completed yet, or a server that does
    /// not send it; a measured mean is never 0.
    /// </summary>
    [ProtoMember(5)] public uint TickMicros { get; set; }

    public static OutboundPacket Create(long serverTicks, long clientTicks, long rtt, long offset, uint tickMicros,
        PacketEncoder encoder)
    {
        SPingPacket message = PacketEncoder.Scratch<SPingPacket>();
        message.ServerTimestamp = serverTicks;
        message.ClientTimestamp = clientTicks;
        message.Rtt = rtt;
        message.Offset = offset;
        message.TickMicros = tickMicros;
        return encoder.Encode(message, PacketType, NetworkPacketFlags.None, Protocol);
    }
}
