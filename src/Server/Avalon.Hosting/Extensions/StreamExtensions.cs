using System.Buffers;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Hosting.Extensions;

internal static class StreamExtensions
{
    [ThreadStatic] private static PooledArrayBufferWriter? _tempWriter;
    [ThreadStatic] private static PooledArrayBufferWriter? _burstWriter;

    internal static PooledArrayBufferWriter BurstWriter
        => _burstWriter ??= new PooledArrayBufferWriter();

    internal static void AppendPacket(NetworkPacket packet)
    {
        var temp = _tempWriter ??= new PooledArrayBufferWriter();
        temp.Reset();
        Serializer.Serialize(temp, packet);

        WriteVarint(BurstWriter, (uint)temp.Written);
        var dest = BurstWriter.GetSpan(temp.Written);
        temp.WrittenSpan.CopyTo(dest);
        BurstWriter.Advance(temp.Written);
    }

    private static void WriteVarint(IBufferWriter<byte> writer, uint value)
    {
        Span<byte> span = writer.GetSpan(5);
        int i = 0;
        while (value > 0x7F) { span[i++] = (byte)((value & 0x7F) | 0x80); value >>= 7; }
        span[i++] = (byte)value;
        writer.Advance(i);
    }
}
