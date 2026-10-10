using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Serialization;

/// <summary>
/// Encodes a server packet's message into a pooled payload segment (#875): protobuf-net into one stream per thread,
/// rewound for every packet (#640: through a stream protobuf-net allocates nothing for nested messages), then copied
/// into a segment of <see cref="Pool" />. No sealing and no array per packet: the send path seals, when its connection
/// does, as it frames the packet.
/// </summary>
public sealed class PacketEncoder(PayloadSegmentPool pool)
{
    // [ThreadStatic] fields take the t_ prefix, which a naming rule cannot select (the static-field rule asks for s_).
#pragma warning disable IDE1006
    [ThreadStatic] private static MemoryStream? t_stream;
#pragma warning restore IDE1006

    /// <summary>The encoder every server packet uses, over <see cref="PayloadSegmentPool.Shared" />.</summary>
    public static PacketEncoder Shared { get; } = new(PayloadSegmentPool.Shared);

    public PayloadSegmentPool Pool { get; } = pool;

    public OutboundPacket Encode<T>(T message, NetworkPacketType type, NetworkPacketFlags flags, NetworkProtocol protocol)
        where T : class
    {
        MemoryStream stream = t_stream ??= new MemoryStream(512);
        stream.SetLength(0);
        Serializer.Serialize(stream, message);
        PayloadSegment payload = Pool.Rent(new ReadOnlySpan<byte>(stream.GetBuffer(), 0, (int)stream.Length));
        return new OutboundPacket(
            new NetworkPacketHeader { Type = type, Flags = flags, Protocol = protocol, Version = 0 }, payload);
    }
}
