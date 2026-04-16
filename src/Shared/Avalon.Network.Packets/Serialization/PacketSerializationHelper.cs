using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Serialization;

internal static class PacketSerializationHelper
{
    [ThreadStatic] private static PooledArrayBufferWriter? _writer;

    public static NetworkPacket Serialize<T>(
        T packet,
        NetworkPacketType type,
        NetworkPacketFlags flags,
        NetworkProtocol protocol,
        EncryptFunc encrypt) where T : class
    {
        var writer = _writer ??= new PooledArrayBufferWriter();
        writer.Reset();
        Serializer.Serialize(writer, packet);
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader { Type = type, Flags = flags, Protocol = protocol, Version = 0 },
            Payload = encrypt(writer.WrittenSpan)
        };
    }

    public static NetworkPacket SerializeUnencrypted<T>(
        T packet,
        NetworkPacketType type,
        NetworkPacketFlags flags,
        NetworkProtocol protocol) where T : class
    {
        var writer = _writer ??= new PooledArrayBufferWriter();
        writer.Reset();
        Serializer.Serialize(writer, packet);
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader { Type = type, Flags = flags, Protocol = protocol, Version = 0 },
            Payload = writer.WrittenSpan.ToArray()
        };
    }
}
