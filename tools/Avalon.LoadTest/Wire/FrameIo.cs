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
/// <remarks>
/// Asynchronous throughout, so a waiting bot holds no thread and its token is honoured while it waits. Each frame is
/// read into one buffer the reader keeps; that is safe because protobuf-net copies a bytes field into a new array, so
/// a returned packet's <see cref="NetworkPacket.Payload"/> shares nothing with the buffer the next frame overwrites.
/// </remarks>
public sealed class FrameReader(Stream stream)
{
    // Far above any packet a server sends; a larger length is a corrupt or hostile stream, not a frame to allocate for.
    private const int MaxFrameLength = 1024 * 1024;

    // Buffered so the varint's bytes and the frame come from one read of the socket where possible.
    private readonly BufferedStream _buffered = new(stream, 64 * 1024);
    private readonly byte[] _lengthByte = new byte[1];
    private byte[] _frame = new byte[4096];

    /// <summary>The next packet, or null when the stream ends cleanly between frames.</summary>
    /// <exception cref="EndOfStreamException">The stream ended inside a frame.</exception>
    /// <exception cref="InvalidDataException">A frame's length is malformed or above the limit.</exception>
    public async ValueTask<NetworkPacket?> ReadAsync(CancellationToken ct)
    {
        int length = await ReadLengthAsync(ct).ConfigureAwait(false);
        if (length < 0) return null;

        if (_frame.Length < length) _frame = new byte[Math.Max(length, _frame.Length * 2)];
        await _buffered.ReadExactlyAsync(_frame.AsMemory(0, length), ct).ConfigureAwait(false);
        return Serializer.Deserialize<NetworkPacket>(new ReadOnlySpan<byte>(_frame, 0, length));
    }

    // The base-128 varint length, byte by byte from the buffer; -1 for a clean end of stream before its first byte.
    private async ValueTask<int> ReadLengthAsync(CancellationToken ct)
    {
        ulong value = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            if (await _buffered.ReadAsync(_lengthByte, ct).ConfigureAwait(false) == 0)
            {
                return shift == 0 ? -1 : throw new EndOfStreamException("The stream ended inside a frame's length.");
            }

            byte next = _lengthByte[0];
            value |= (ulong)(next & 0x7F) << shift;
            if ((next & 0x80) == 0)
            {
                return value <= MaxFrameLength
                    ? (int)value
                    : throw new InvalidDataException($"A frame of {value} bytes is above the {MaxFrameLength}-byte limit.");
            }
        }

        throw new InvalidDataException("A frame's length runs past five bytes.");
    }
}
