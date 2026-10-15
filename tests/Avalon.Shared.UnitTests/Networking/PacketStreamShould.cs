using System.Globalization;
using System.Threading.Channels;
using Avalon.Hosting.Networking;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Avalon.Shared.UnitTests.Networking;

public class PacketStreamShould
{
    // Three frames laid out back-to-back: each is varint(len) + len bytes of payload.
    // Frame layouts: [1, 0xAA], [3, 0xBB, 0xCC, 0xDD], [2, 0xEE, 0xFF]
    private static readonly byte[] s_threeFrames =
    {
        0x01, 0xAA,
        0x03, 0xBB, 0xCC, 0xDD,
        0x02, 0xEE, 0xFF
    };

    [Fact]
    public async Task Yield_All_Frames_When_Stream_Returns_All_Bytes_At_Once()
    {
        using var ms = new MemoryStream(s_threeFrames);
        var ps = new PacketStream(ms);

        var frames = new System.Collections.Generic.List<byte[]>();
        await foreach (ReadOnlyMemory<byte> frame in ps.EnumerateRawFramesAsync(64, 1024, CancellationToken.None))
            frames.Add(frame.ToArray());

        Assert.Equal(3, frames.Count);
        Assert.Equal(new byte[] { 0xAA }, frames[0]);
        Assert.Equal(new byte[] { 0xBB, 0xCC, 0xDD }, frames[1]);
        Assert.Equal(new byte[] { 0xEE, 0xFF }, frames[2]);
    }

    [Fact]
    public async Task Yield_All_Frames_When_Stream_Drips_One_Byte_At_A_Time()
    {
        using var drip = new DrippingStream(s_threeFrames);
        var ps = new PacketStream(drip);

        var frames = new System.Collections.Generic.List<byte[]>();
        await foreach (ReadOnlyMemory<byte> frame in ps.EnumerateRawFramesAsync(64, 1024, CancellationToken.None))
            frames.Add(frame.ToArray());

        Assert.Equal(3, frames.Count);
        Assert.Equal(new byte[] { 0xAA }, frames[0]);
        Assert.Equal(new byte[] { 0xBB, 0xCC, 0xDD }, frames[1]);
        Assert.Equal(new byte[] { 0xEE, 0xFF }, frames[2]);
    }

    /// <summary>A frame of exactly the maximum length is read whole: the maximum is the last length accepted.</summary>
    [Fact]
    public async Task Yield_Multibyte_Varint_Length_Frames()
    {
        // varint(200) = 0xC8 0x01 ; payload = 200 bytes of 0x42
        byte[] payload = new byte[200];
        Array.Fill(payload, (byte)0x42);
        var ms = new MemoryStream();
        ms.WriteByte(0xC8);
        ms.WriteByte(0x01);
        ms.Write(payload, 0, payload.Length);
        ms.Position = 0;
        var ps = new PacketStream(ms);

        var frames = new System.Collections.Generic.List<byte[]>();
        await foreach (ReadOnlyMemory<byte> frame in ps.EnumerateRawFramesAsync(64, 200, CancellationToken.None))
            frames.Add(frame.ToArray());

        Assert.Single(frames);
        Assert.Equal(payload, frames[0]);
        Assert.False(ps.FrameRefused);
    }

    /// <summary>
    /// A frame's length prefix is the peer's word, and any peer that completes TLS can send one. A frame that declares
    /// more than the maximum, or whose prefix is no length at all, ends the stream there: the frames before it were
    /// yielded, nothing is thrown, and the stream is never asked for a read sized by the declared length, as it was
    /// when a buffer of that size was rented on the prefix alone.
    /// </summary>
    [Theory]
    [InlineData(new byte[] { 0xC9, 0x01 }, 201L)]                               // one more than the maximum
    [InlineData(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x04 }, 1L << 30)]         // 1 GiB
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F }, 0xFFFFFFFFL)]      // -1 as the 32-bit length it was read as
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x7F }, 0x7FFFFFFFFL)]     // 35 bits: past any 32-bit length
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 }, PacketStream.MalformedLength)] // six bytes: no length
    public async Task End_the_stream_at_a_frame_whose_declared_length_is_over_the_maximum_or_malformed(
        byte[] lengthPrefix, long declared)
    {
        const int Maximum = 200;
        byte[] wire = [0x01, 0xAA, .. lengthPrefix, .. new byte[4096]];
        using var stream = new ReadSizeRecordingStream(wire);
        var ps = new PacketStream(stream);

        var frames = new System.Collections.Generic.List<byte[]>();
        await foreach (ReadOnlyMemory<byte> frame in ps.EnumerateRawFramesAsync(64, Maximum, CancellationToken.None))
            frames.Add(frame.ToArray());

        Assert.Equal(new byte[] { 0xAA }, Assert.Single(frames));
        Assert.True(ps.FrameRefused);
        Assert.Equal(declared, ps.RefusedFrameLength);
        // The pool hands out at least what was asked for, so the initial buffer is the measure, with slack.
        Assert.InRange(stream.LargestReadRequested, 1, 1024);
    }

    /// <summary>
    /// A frame the peer started has <c>Hosting:InboundFrameTimeoutSeconds</c> to complete, from its first byte (#938); one
    /// still incomplete then ends the stream, which closes the connection. The wait between frames is never timed. The
    /// script feeds the wire's next bytes ("4") or moves the clock ("@59.999", in seconds); each piece fed is read before
    /// the script goes on. The wire is two frames, of four bytes and of three. The deadline is longer than the hang guard,
    /// so one kept on the system clock rather than the stream's would not expire within the test.
    /// </summary>
    [Theory]
    [InlineData("4 @600 3", 2, false)]               // whole frames, idle far past the deadline between them
    [InlineData("2 @59.999 3 @59.999 2", 2, false)]  // each frame in pieces, in time; the second's clock starts at its own first byte
    [InlineData("2 @1 3 @60", 1, true)]              // a frame started and abandoned, after one that arrived in pieces
    [InlineData("1 @24 1 @24 1 @24", 0, true)]       // a frame trickled a byte at a time, slower than the deadline
    public async Task End_the_stream_at_a_frame_not_completed_within_the_deadline(string script, int framesRead,
        bool timedOut)
    {
        byte[] wire = [0x03, 0xA1, 0xA2, 0xA3, 0x02, 0xB1, 0xB2];
        var time = new FakeTimeProvider();
        using var fed = new FedStream();
        var ps = new PacketStream(fed);
        var frames = new System.Collections.Generic.List<byte[]>();
        var reading = Task.Run(async () =>
        {
            await foreach (ReadOnlyMemory<byte> frame in ps.EnumerateRawFramesAsync(64, 1024, TimeSpan.FromSeconds(60),
                               time, CancellationToken.None))
            {
                frames.Add(frame.ToArray());
            }
        });

        await fed.ReadingAsync();
        int sent = 0;
        foreach (string step in script.Split(' '))
        {
            if (step[0] == '@')
            {
                time.Advance(TimeSpan.FromSeconds(double.Parse(step[1..], CultureInfo.InvariantCulture)));
                continue;
            }

            int count = int.Parse(step, CultureInfo.InvariantCulture);
            fed.Feed(wire[sent..(sent + count)]);
            sent += count;
            await fed.ReadingAsync();
        }

        // A stream that timed out has ended by itself; one that did not ends when the peer does.
        if (!timedOut)
            fed.End();
        await reading.WaitAsync(s_guard);

        Assert.Equal(framesRead, frames.Count);
        Assert.Equal(timedOut, ps.FrameTimedOut);
    }

    /// <summary>A hang guard only: nothing waits on it when the stream behaves.</summary>
    private static readonly TimeSpan s_guard = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A stream a test feeds piece by piece. A read waits for the next piece, honouring its token, and signals as it
    /// starts to wait, so the test knows the read loop has taken everything fed so far.
    /// </summary>
    private sealed class FedStream : Stream
    {
        private readonly Channel<byte[]> _pieces = Channel.CreateUnbounded<byte[]>();
        private readonly SemaphoreSlim _waiting = new(0);
        private byte[] _piece = [];
        private int _offset;

        public void Feed(byte[] piece) => _pieces.Writer.TryWrite(piece);

        public void End() => _pieces.Writer.TryComplete();

        public async Task ReadingAsync() => Assert.True(await _waiting.WaitAsync(s_guard), "the stream was not read");

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_offset == _piece.Length)
            {
                if (!_pieces.Reader.TryRead(out byte[]? next))
                {
                    _waiting.Release();
                    if (!await _pieces.Reader.WaitToReadAsync(cancellationToken))
                        return 0;
                    _pieces.Reader.TryRead(out next);
                }

                _piece = next!;
                _offset = 0;
            }

            int count = Math.Min(buffer.Length, _piece.Length - _offset);
            _piece.AsSpan(_offset, count).CopyTo(buffer.Span);
            _offset += count;
            return count;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _waiting.Dispose();
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A stream over fixed bytes that records the largest read it was asked for.</summary>
    private sealed class ReadSizeRecordingStream(byte[] data) : MemoryStream(data)
    {
        public int LargestReadRequested { get; private set; }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            LargestReadRequested = Math.Max(LargestReadRequested, buffer.Length);
            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    /// <summary>Stream that returns at most one byte per ReadAsync call.</summary>
    private sealed class DrippingStream : Stream
    {
        private readonly byte[] _data;
        private int _pos;
        public DrippingStream(byte[] data) { _data = data; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_pos >= _data.Length) return ValueTask.FromResult(0);
            buffer.Span[0] = _data[_pos++];
            return ValueTask.FromResult(1);
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
