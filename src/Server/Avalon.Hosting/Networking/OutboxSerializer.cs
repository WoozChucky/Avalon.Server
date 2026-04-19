using System.Buffers;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Hosting.Networking;

public static class OutboxSerializer
{
    public static void AppendPacket(
        PooledArrayBufferWriter burst,
        PooledArrayBufferWriter temp,
        NetworkPacket packet)
    {
        temp.Reset();
        Serializer.Serialize(temp, packet);
        WriteVarint(burst, (uint)temp.Written);
        var dest = burst.GetSpan(temp.Written);
        temp.WrittenSpan.CopyTo(dest);
        burst.Advance(temp.Written);
    }

    public static void WriteVarint(IBufferWriter<byte> writer, uint value)
    {
        Span<byte> span = writer.GetSpan(5);
        int i = 0;
        while (value > 0x7F) { span[i++] = (byte)((value & 0x7F) | 0x80); value >>= 7; }
        span[i++] = (byte)value;
        writer.Advance(i);
    }
}
