// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Avalon.Shared.UnitTests.Networking;

public class ChannelOutboxShould
{
    [Fact]
    public async Task WriteEnqueuedPacket_ToStream_AfterConnect()
    {
        var ms = new MemoryStream();
        var stream = new PacketStream(ms);
        var outbox = new ChannelOutbox(Guid.NewGuid(), NullLogger.Instance, capacity: 64);

        outbox.Connect(stream);

        outbox.Enqueue(SPingPacket.Create(0L, 0L, 0L, 0L));

        // Poll until bytes arrive or 2s elapses
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (ms.Length == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        Assert.True(ms.Length > 0, "Expected bytes written to stream");
        await outbox.DisposeAsync();
    }

    /// <summary>
    /// A rejection packet is queued and the connection closed on the next line, so disposal
    /// races the drain loop. The write is slow enough that the loop cannot have finished it
    /// before disposal begins — the packet only arrives if disposal waits for the drain.
    /// </summary>
    [Fact]
    public async Task DeliverQueuedPacket_WhenDisposedImmediatelyAfterEnqueue()
    {
        var sink = new SlowStream(TimeSpan.FromMilliseconds(50));
        var stream = new PacketStream(sink);
        var outbox = new ChannelOutbox(Guid.NewGuid(), NullLogger.Instance, capacity: 64);

        outbox.Connect(stream);

        outbox.Enqueue(SPingPacket.Create(0L, 0L, 0L, 0L));
        await outbox.DisposeAsync();

        Assert.True(sink.BytesWritten > 0, "Expected the queued packet to reach the stream before the outbox closed");
    }

    /// <summary>
    /// A completed and drained queue is the normal end of the send loop, not a fault.
    /// </summary>
    [Fact]
    public async Task NotLogAnError_WhenDisposedAfterDelivery()
    {
        var logger = new CapturingLogger();
        var sink = new SlowStream(TimeSpan.FromMilliseconds(10));
        var stream = new PacketStream(sink);
        var outbox = new ChannelOutbox(Guid.NewGuid(), logger, capacity: 64);

        outbox.Connect(stream);

        outbox.Enqueue(SPingPacket.Create(0L, 0L, 0L, 0L));
        await outbox.DisposeAsync();

        // The fault continuation runs after the loop ends; give it a moment to land.
        await Task.Delay(100);

        Assert.DoesNotContain(LogLevel.Error, logger.Levels);
    }

    /// <summary>
    /// A peer that has stopped reading must not hold the close open: the flush is bounded.
    /// </summary>
    [Fact]
    public async Task ReturnWithinFlushBudget_WhenTheWriteNeverCompletes()
    {
        var sink = new BlockingStream();
        var stream = new PacketStream(sink);
        var outbox = new ChannelOutbox(Guid.NewGuid(), NullLogger.Instance, capacity: 64);

        try
        {
            outbox.Connect(stream);
            outbox.Enqueue(SPingPacket.Create(0L, 0L, 0L, 0L));

            var sw = Stopwatch.StartNew();
            await outbox.DisposeAsync();
            sw.Stop();

            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2),
                $"Expected disposal to give up on the stalled write, took {sw.ElapsedMilliseconds}ms");
        }
        finally
        {
            sink.Release();
        }
    }

    /// <summary>Writes complete, but only after a delay — a socket write that is not instantaneous.</summary>
    private sealed class SlowStream(TimeSpan delay) : Stream
    {
        private long _bytesWritten;

        public long BytesWritten => Interlocked.Read(ref _bytesWritten);

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            Interlocked.Add(ref _bytesWritten, buffer.Length);
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    }

    /// <summary>A peer that has stopped reading: the write never completes and ignores cancellation.</summary>
    private sealed class BlockingStream : Stream
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _gate.TrySetResult();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => await _gate.Task.ConfigureAwait(false);

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly List<LogLevel> _levels = [];

        public IReadOnlyList<LogLevel> Levels
        {
            get { lock (_levels) return [.. _levels]; }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_levels) _levels.Add(logLevel);
        }
    }
}
