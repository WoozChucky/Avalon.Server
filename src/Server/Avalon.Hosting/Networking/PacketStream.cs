using System.Buffers;
using System.Runtime.CompilerServices;

namespace Avalon.Hosting.Networking;

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

    /// <summary>A length prefix is a varint of at most this many bytes: 32 bits, seven to a byte.</summary>
    private const int MaxLengthPrefixBytes = 5;

    /// <summary>What <see cref="RefusedFrameLength" /> reads for a length prefix that is not a length at all.</summary>
    public const long MalformedLength = long.MaxValue;

    /// <summary>
    /// Whether the enumeration ended on a frame it would not read: one that declared more than the maximum, or whose
    /// length prefix was malformed. Nothing was buffered for it; the stream is at no frame boundary any more and
    /// cannot be read further.
    /// </summary>
    public bool FrameRefused { get; private set; }

    /// <summary>The length the refused frame declared, or <see cref="MalformedLength" />; 0 while none was refused.</summary>
    public long RefusedFrameLength { get; private set; }

    /// <summary>
    /// Yields raw payload slices (varint length-prefix stripped). Slice memory is
    /// valid only until the next iteration — consumers must materialize before advancing.
    /// Public for testability of the buffered read loop independent of frame parsing.
    /// </summary>
    /// <param name="initialBufferSize">The read buffer to start with; it grows to the largest frame read.</param>
    /// <param name="maxFrameLength">
    /// The most bytes a frame may declare after its length prefix. A frame that declares more, or whose prefix is
    /// not a length, ends the enumeration before anything is rented or read for it (<see cref="FrameRefused" />):
    /// the prefix is the peer's word, and a buffer of that size was otherwise rented on it alone.
    /// </param>
    /// <param name="token">Ends the enumeration.</param>
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> EnumerateRawFramesAsync(int initialBufferSize,
        int maxFrameLength, [EnumeratorCancellation] CancellationToken token = default)
    {
        // Whatever the caller asks, a frame and its prefix fit one array.
        long maxDeclared = Math.Min(maxFrameLength, Array.MaxLength - MaxLengthPrefixBytes);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(initialBufferSize);
        int dataStart = 0;   // first unread byte
        int dataEnd = 0;     // first empty byte
        try
        {
            while (true)
            {
                // 1) Try to parse varint length from already-buffered bytes.
                int varintBytes = TryReadLength(buffer.AsSpan(dataStart, dataEnd - dataStart), out long declared);
                if (varintBytes == 0)
                {
                    // Need more bytes for the varint. Refill or yield break on EOF/error.
                    dataEnd = await RefillAsync(buffer, dataEnd, token).ConfigureAwait(false);
                    if (dataEnd < 0)
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

                // The one check a frame's length gets, before anything is sized by it. A malformed prefix reads as
                // more than any maximum, so it ends here too.
                if (declared > maxDeclared)
                {
                    RefusedFrameLength = declared;
                    FrameRefused = true;
                    yield break;
                }

                int payloadLen = (int)declared;
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
                    dataEnd = await RefillAsync(buffer, dataEnd, token).ConfigureAwait(false);
                    if (dataEnd < 0)
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

    /// <summary>
    /// Reads what the stream has into the free end of <paramref name="buffer" />. Returns the new end of the data, or
    /// -1 once the stream has ended (the peer closed it, or it was disposed or cancelled). Called once per read, so per
    /// packet or so: it returns the new end rather than handing it to a callback, which was a delegate per read, and
    /// its state machine is pooled, which is a box per read that did not complete at once otherwise (#875).
    /// </summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private async ValueTask<int> RefillAsync(byte[] buffer, int dataEnd, CancellationToken token)
    {
        int free = buffer.Length - dataEnd;
        if (free == 0)
        {
            // Caller is responsible for compaction/growth; we only get here on a full buffer.
            return dataEnd;
        }

        int read;
        try
        {
            read = await stream.ReadAsync(buffer.AsMemory(dataEnd, free), token).ConfigureAwait(false);
        }
        catch (ObjectDisposedException) { return -1; }
        catch (IOException) { return -1; }
        catch (OperationCanceledException) { return -1; }

        if (read == 0) return -1; // peer closed
        return dataEnd + read;
    }

    /// <summary>
    /// Reads a frame's length prefix, a varint, from the front of <paramref name="span" />. Returns the bytes it
    /// took, or 0 when more bytes are needed. The length is a <see cref="long" /> and never negative: five bytes carry
    /// 35 bits, more than an <see cref="int" /> holds, and the caller compares before it narrows. A prefix that runs
    /// past five bytes is no length: it reads as <see cref="MalformedLength" />, which no maximum admits, rather than
    /// throwing, so the caller has one refusal to handle.
    /// </summary>
    private static int TryReadLength(ReadOnlySpan<byte> span, out long length)
    {
        length = 0;
        int available = Math.Min(span.Length, MaxLengthPrefixBytes);
        for (int i = 0; i < available; i++)
        {
            byte b = span[i];
            length |= (long)(b & 0x7F) << (7 * i);
            if ((b & 0x80) == 0) return i + 1;
        }

        if (available < MaxLengthPrefixBytes) return 0;
        length = MalformedLength;
        return MaxLengthPrefixBytes;
    }

}
