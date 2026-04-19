// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Avalon.Hosting.Networking;

using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

public class PacketStream(Stream stream) : Stream
{
    public override void Flush() => stream.Flush();

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => stream.WriteAsync(buffer, cancellationToken);

    public override Task FlushAsync(CancellationToken cancellationToken)
        => stream.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => stream.Read(buffer, offset, count);

    public override long Seek(long offset, SeekOrigin origin) => stream.Seek(offset, origin);

    public override void SetLength(long value) => stream.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count) => stream.Write(buffer, offset, count);

    public override bool CanRead => stream.CanRead;
    public override bool CanSeek => stream.CanSeek;
    public override bool CanWrite => stream.CanWrite;
    public override long Length => stream.Length;
    public override long Position { get => stream.Position; set => stream.Position = value; }

    /// <summary>
    /// Yields raw payload slices (varint length-prefix stripped). Slice memory is
    /// valid only until the next iteration — consumers must materialize before advancing.
    /// Public for testability of the buffered read loop independent of frame parsing.
    /// </summary>
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> EnumerateRawFramesAsync(int initialBufferSize,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(initialBufferSize);
        int dataStart = 0;   // first unread byte
        int dataEnd = 0;     // first empty byte
        try
        {
            while (true)
            {
                // 1) Try to parse varint length from already-buffered bytes.
                (int packetSize, int varintLen)? lenResult = TryReadVarint(buffer.AsSpan(dataStart, dataEnd - dataStart));
                if (lenResult is null)
                {
                    // Need more bytes for the varint. Refill or yield break on EOF/error.
                    if (!await RefillAsync(buffer, dataStart, dataEnd, refilled => dataEnd = refilled, token).ConfigureAwait(false))
                        yield break;

                    // Compact if we filled the tail.
                    if (dataEnd == buffer.Length && dataStart > 0)
                    {
                        Buffer.BlockCopy(buffer, dataStart, buffer, 0, dataEnd - dataStart);
                        dataEnd -= dataStart;
                        dataStart = 0;
                    }
                    continue;
                }

                (int payloadLen, int varintBytes) = lenResult.Value;
                int frameTotal = varintBytes + payloadLen;

                // 2) Grow buffer if the frame is larger than current capacity.
                if (frameTotal > buffer.Length)
                {
                    int newSize = Math.Max(buffer.Length * 2, frameTotal);
                    byte[] newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
                    Buffer.BlockCopy(buffer, dataStart, newBuffer, 0, dataEnd - dataStart);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = newBuffer;
                    dataEnd -= dataStart;
                    dataStart = 0;
                }

                // 3) Read until we have the full frame in the buffer.
                while (dataEnd - dataStart < frameTotal)
                {
                    if (!await RefillAsync(buffer, dataStart, dataEnd, refilled => dataEnd = refilled, token).ConfigureAwait(false))
                        yield break;

                    if (dataEnd == buffer.Length && dataStart > 0)
                    {
                        Buffer.BlockCopy(buffer, dataStart, buffer, 0, dataEnd - dataStart);
                        dataEnd -= dataStart;
                        dataStart = 0;
                    }
                }

                // 4) Yield the payload slice (skip the varint header).
                yield return new ReadOnlyMemory<byte>(buffer, dataStart + varintBytes, payloadLen);
                dataStart += frameTotal;

                // 5) Reset offsets when the buffer is drained — avoids unnecessary compaction.
                if (dataStart == dataEnd)
                {
                    dataStart = 0;
                    dataEnd = 0;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async ValueTask<bool> RefillAsync(byte[] buffer, int dataStart, int dataEnd,
        Action<int> setDataEnd, CancellationToken token)
    {
        int free = buffer.Length - dataEnd;
        if (free == 0)
        {
            // Caller is responsible for compaction/growth; we only get here on a full buffer.
            return true;
        }

        int read;
        try
        {
            read = await stream.ReadAsync(buffer.AsMemory(dataEnd, free), token).ConfigureAwait(false);
        }
        catch (ObjectDisposedException) { return false; }
        catch (IOException) { return false; }
        catch (OperationCanceledException) { return false; }

        if (read == 0) return false; // peer closed
        setDataEnd(dataEnd + read);
        return true;
    }

    /// <summary>
    /// Attempts to read a varint from the front of <paramref name="span"/>. Returns null
    /// if more bytes are needed. Returns (value, bytesConsumed) on success.
    /// </summary>
    private static (int value, int bytesConsumed)? TryReadVarint(ReadOnlySpan<byte> span)
    {
        int value = 0;
        int shift = 0;
        for (int i = 0; i < span.Length; i++)
        {
            byte b = span[i];
            value |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) return (value, i + 1);
            shift += 7;
            if (shift >= 35) throw new InvalidDataException("Varint too large for int32.");
        }
        return null;
    }

}
