using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Serialization;

/// <summary>
/// Serializes the client-to-server packets (<c>C*Packet.Create</c>), which only clients send: the load-test tool, the
/// benchmarks and the tests. Server packets go through <see cref="PacketEncoder" />.
/// </summary>
internal static class PacketSerializationHelper
{
    /// <summary>
    /// One stream per thread, rewound for every packet. A stream rather than a buffer writer (#640):
    /// through a buffer writer protobuf-net measures every nested message before writing it, which
    /// allocated about 250 bytes per entity in a state broadcast; through a stream it writes the same
    /// bytes and allocates nothing. The stream keeps the largest packet it has held, as the writer did.
    /// </summary>
    // [ThreadStatic] fields take the t_ prefix, which a naming rule cannot select (the static-field rule asks for s_).
#pragma warning disable IDE1006
    [ThreadStatic] private static MemoryStream? t_stream;
#pragma warning restore IDE1006

    public static NetworkPacket Serialize<T>(
        T packet,
        NetworkPacketType type,
        NetworkPacketFlags flags,
        NetworkProtocol protocol,
        EncryptFunc encrypt) where T : class
    {
        MemoryStream stream = Write(packet);
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader { Type = type, Flags = flags, Protocol = protocol, Version = 0 },
            Payload = encrypt(Written(stream))
        };
    }

    public static NetworkPacket SerializeUnencrypted<T>(
        T packet,
        NetworkPacketType type,
        NetworkPacketFlags flags,
        NetworkProtocol protocol) where T : class
    {
        MemoryStream stream = Write(packet);
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader { Type = type, Flags = flags, Protocol = protocol, Version = 0 },
            Payload = Written(stream).ToArray()
        };
    }

    private static MemoryStream Write<T>(T packet) where T : class
    {
        MemoryStream stream = t_stream ??= new MemoryStream(512);
        stream.SetLength(0);
        Serializer.Serialize(stream, packet);
        return stream;
    }

    private static ReadOnlySpan<byte> Written(MemoryStream stream) =>
        new(stream.GetBuffer(), 0, (int)stream.Length);
}
