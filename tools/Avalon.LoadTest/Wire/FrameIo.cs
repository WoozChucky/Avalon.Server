using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.LoadTest.Wire;

/// <summary>Writes frames as the servers read them: a <see cref="NetworkPacket"/> behind a base-128 varint length.</summary>
public static class FrameIo
{
    public static async ValueTask WriteAsync(Stream stream, NetworkPacket packet, CancellationToken ct)
    {
        // One buffered write per frame, so a TLS stream sends one record per packet.
        using var buffer = new MemoryStream(64);
        Serializer.SerializeWithLengthPrefix(buffer, packet, PrefixStyle.Base128);
        await stream.WriteAsync(buffer.GetBuffer().AsMemory(0, (int)buffer.Length), ct).ConfigureAwait(false);
    }
}

/// <summary>Reads the frames a server writes, one <see cref="NetworkPacket"/> at a time.</summary>
public sealed class FrameReader(Stream stream)
{
    // Buffered so the varint and the frame come from one read where possible.
    private readonly BufferedStream _buffered = new(stream, 64 * 1024);

    /// <summary>The next packet, or null at the end of the stream.</summary>
    public ValueTask<NetworkPacket?> ReadAsync(CancellationToken ct) => new(Task.Run<NetworkPacket?>(() =>
        Serializer.DeserializeWithLengthPrefix<NetworkPacket>(_buffered, PrefixStyle.Base128), ct));
}
