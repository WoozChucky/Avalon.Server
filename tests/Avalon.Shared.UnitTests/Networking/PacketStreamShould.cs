// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalon.Hosting.Networking;
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
        await foreach (var frame in ps.EnumerateRawFramesAsync(64, CancellationToken.None))
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
        await foreach (var frame in ps.EnumerateRawFramesAsync(64, CancellationToken.None))
            frames.Add(frame.ToArray());

        Assert.Equal(3, frames.Count);
        Assert.Equal(new byte[] { 0xAA }, frames[0]);
        Assert.Equal(new byte[] { 0xBB, 0xCC, 0xDD }, frames[1]);
        Assert.Equal(new byte[] { 0xEE, 0xFF }, frames[2]);
    }

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
        await foreach (var frame in ps.EnumerateRawFramesAsync(64, CancellationToken.None))
            frames.Add(frame.ToArray());

        Assert.Single(frames);
        Assert.Equal(payload, frames[0]);
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
