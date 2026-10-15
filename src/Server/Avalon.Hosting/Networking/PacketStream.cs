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
    /// Whether the enumeration ended on a frame the peer started and did not complete within the deadline it was given
    /// (<c>Hosting:InboundFrameTimeoutSeconds</c>, #938). The stream is at no frame boundary any more and cannot be read
    /// further. A frame that completes as the deadline expires may end it too: it is the deadline's edge.
    /// </summary>
    public bool FrameTimedOut { get; private set; }

    /// <summary>
    /// Yields raw payload slices (varint length-prefix stripped), with no deadline for a frame to complete: for reading
    /// a stream whose writer is trusted, what a server wrote in tests and tools. A server's read loop uses the overload
    /// with a deadline, since a peer that starts a frame and stops would otherwise hold its connection for as long as
    /// it likes.
    /// </summary>
    public IAsyncEnumerable<ReadOnlyMemory<byte>> EnumerateRawFramesAsync(int initialBufferSize, int maxFrameLength,
        CancellationToken token = default) =>
        EnumerateRawFramesAsync(initialBufferSize, maxFrameLength, Timeout.InfiniteTimeSpan, TimeProvider.System, token);

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
    /// <param name="frameDeadline">
    /// How long a frame has to complete once its first byte has arrived, or <see cref="Timeout.InfiniteTimeSpan" /> for
    /// no limit. A frame still incomplete after it ends the enumeration (<see cref="FrameTimedOut" />). Nothing times the
    /// wait between frames.
    /// </param>
    /// <param name="timeProvider">The clock the deadline runs on.</param>
    /// <param name="token">Ends the enumeration.</param>
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> EnumerateRawFramesAsync(int initialBufferSize,
        int maxFrameLength, TimeSpan frameDeadline, TimeProvider timeProvider,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        // Whatever the caller asks, a frame and its prefix fit one array.
        long maxDeclared = Math.Min(maxFrameLength, Array.MaxLength - MaxLengthPrefixBytes);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(initialBufferSize);
        int dataStart = 0;   // first unread byte
        int dataEnd = 0;     // first empty byte
        FrameDeadline? deadline = frameDeadline == Timeout.InfiniteTimeSpan
            ? null
            : new FrameDeadline(frameDeadline, timeProvider, token);
        try
        {
            while (true)
            {
                // 1) Try to parse varint length from already-buffered bytes.
                int varintBytes = TryReadLength(buffer.AsSpan(dataStart, dataEnd - dataStart), out long declared);
                if (varintBytes == 0)
                {
                    // Need more bytes for the varint. Refill or yield break on EOF/error.
                    deadline?.BeforeRead(dataEnd - dataStart);
                    dataEnd = await RefillAsync(buffer, dataEnd, deadline?.ReadToken ?? token).ConfigureAwait(false);
                    if (dataEnd < 0)
                    {
                        FrameTimedOut = deadline?.Expired == true;
                        yield break;
                    }

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
                    deadline?.BeforeRead(dataEnd - dataStart);
                    dataEnd = await RefillAsync(buffer, dataEnd, deadline?.ReadToken ?? token).ConfigureAwait(false);
                    if (dataEnd < 0)
                    {
                        FrameTimedOut = deadline?.Expired == true;
                        yield break;
                    }

                    if (dataEnd == buffer.Length && dataStart > 0)
                    {
                        Buffer.BlockCopy(buffer, dataStart, buffer, 0, dataEnd - dataStart);
                        dataEnd -= dataStart;
                        dataStart = 0;
                    }
                }

                // The frame is whole: its clock stops here, before the consumer has it, so what the consumer does with
                // it is never counted against the peer.
                deadline?.FrameCompleted();

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
            deadline?.Dispose();
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// One enumeration's deadline for completing a started frame (#938). The clock starts at the read that waits with
    /// bytes of a frame in hand, so at the read after the one that brought the frame's first byte, and stops when the
    /// frame is whole; a read between frames is not timed. The clock is one cancellation source per connection, made
    /// the first time a frame arrives in pieces, and the reads take its token from then on: the stop token cancels it,
    /// so a read still ends with the server's stop. A frame that does not complete in time has its read cancelled, which
    /// ends the enumeration as any ended read does.
    /// <para>
    /// What it costs: a frame that arrives whole in a read is never timed, so it costs a comparison before the next read
    /// and a flag test after the frame. A frame that arrives in pieces moves the source's one timer twice, to its
    /// deadline and back, and allocates nothing. This object is allocated once per connection, and the source, its timer
    /// and the stop registration once more, by the connection's first frame that arrives in pieces. A frame trickled a
    /// byte at a time does not move the timer again: the clock runs from its first byte.
    /// </para>
    /// </summary>
    private sealed class FrameDeadline(TimeSpan limit, TimeProvider timeProvider, CancellationToken stopping)
        : IDisposable
    {
        private CancellationTokenSource? _expiry;
        private CancellationTokenRegistration _stopRegistration;
        private bool _armed;

        /// <summary>The token a read waits with: the deadline's once a frame has arrived in pieces, the stop's until then.</summary>
        public CancellationToken ReadToken => _expiry?.Token ?? stopping;

        /// <summary>The deadline fired, and the server is not stopping.</summary>
        public bool Expired => _expiry is { IsCancellationRequested: true } && !stopping.IsCancellationRequested;

        /// <summary>A read is about to wait with <paramref name="buffered" /> bytes of a frame in hand.</summary>
        public void BeforeRead(int buffered)
        {
            if (buffered == 0 || _armed)
                return;

            if (_expiry is null)
            {
                // Made with the clock, so its timer is the clock's; a disarmed one, until the line below.
                _expiry = new CancellationTokenSource(Timeout.InfiniteTimeSpan, timeProvider);
#pragma warning disable MA0045 // a token callback is synchronous: the stop cancels the read here as it did directly
                _stopRegistration = stopping.UnsafeRegister(static source => ((CancellationTokenSource)source!).Cancel(),
                    _expiry);
#pragma warning restore MA0045
            }

            _expiry.CancelAfter(limit);
            _armed = true;
        }

        /// <summary>The frame in hand is whole.</summary>
        public void FrameCompleted()
        {
            if (!_armed)
                return;

            _expiry!.CancelAfter(Timeout.InfiniteTimeSpan);
            _armed = false;
        }

        public void Dispose()
        {
            // The registration first: once it returns, the stop cannot reach the source it is about to dispose.
            _stopRegistration.Dispose();
            _expiry?.Dispose();
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
