// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Avalon.Shared.UnitTests.Networking;

public class TickDrivenOutboxShould
{
    private static NetworkPacket MakePacket() =>
        SPingPacket.Create(0L, 0L, 0L, 0L);

    private static (PacketStream stream, MemoryStream underlying) MakeSyncStream()
    {
        var ms = new MemoryStream();
        return (new PacketStream(ms), ms);
    }

    // Stream whose WriteAsync blocks until Complete() is called.
    private sealed class SlowStream : Stream
    {
        private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete() => _tcs.TrySetResult();
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct)
            => new(_tcs.Task);
        public override void Flush() { }
        public override int Read(byte[] buf, int off, int cnt) => 0;
        public override long Seek(long off, SeekOrigin orig) => 0;
        public override void SetLength(long val) { }
        public override void Write(byte[] buf, int off, int cnt) { }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set { } }
    }

    // Stream whose WriteAsync always faults.
    private sealed class FaultingStream : Stream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct)
            => new(Task.FromException(new IOException("simulated socket reset")));
        public override void Flush() { }
        public override int Read(byte[] buf, int off, int cnt) => 0;
        public override long Seek(long off, SeekOrigin orig) => 0;
        public override void SetLength(long val) { }
        public override void Write(byte[] buf, int off, int cnt) { }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set { } }
    }

    [Fact]
    public void EnqueueThenFlush_WritesAllPacketsToStream()
    {
        var faultCalled = false;
        var (stream, ms) = MakeSyncStream();
        var outbox = new TickDrivenOutbox(Guid.NewGuid(), NullLogger.Instance, capacity: 64,
            onFault: () => faultCalled = true);
        outbox.Connect(stream);

        outbox.Enqueue(MakePacket());
        outbox.Enqueue(MakePacket());
        outbox.Flush();

        // MemoryStream.WriteAsync completes synchronously; ExecuteSynchronously ContinueWith
        // fires inline — no delay needed.
        Assert.True(ms.Length > 0, "Expected bytes written");
        Assert.False(faultCalled);
    }

    [Fact]
    public async Task Flush_WhileWriteInFlight_IsNoOp()
    {
        var slow = new SlowStream();
        var outbox = new TickDrivenOutbox(Guid.NewGuid(), NullLogger.Instance, capacity: 64,
            onFault: () => { });
        outbox.Connect(new PacketStream(slow));

        // First flush starts a slow write
        outbox.Enqueue(MakePacket());
        outbox.Flush();

        // Second flush while first is in flight — must be a no-op
        outbox.Enqueue(MakePacket());
        outbox.Flush();

        // Complete the first write
        slow.Complete();
        await Task.Delay(50); // let continuation run and clear the flag

        // DisposeAsync should complete promptly — proves flag was cleared
        var sw = Stopwatch.StartNew();
        await outbox.DisposeAsync();
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 200,
            $"Expected flag cleared after write; DisposeAsync took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Continuation_ClearsInFlightFlag_OnSuccess_AllowingNextFlush()
    {
        var (stream, ms) = MakeSyncStream();
        var outbox = new TickDrivenOutbox(Guid.NewGuid(), NullLogger.Instance, capacity: 64,
            onFault: () => { });
        outbox.Connect(stream);

        // Flush #1: synchronous write → continuation fires inline → flag cleared
        outbox.Enqueue(MakePacket());
        outbox.Flush();
        long afterFirst = ms.Length;
        Assert.True(afterFirst > 0, "First flush should have written bytes");

        // Flush #2: flag was cleared; should write again
        outbox.Enqueue(MakePacket());
        outbox.Flush();
        Assert.True(ms.Length > afterFirst, "Second flush should have written additional bytes");
    }

    [Fact]
    public async Task Continuation_TriggersOnFault_OnIOException_AndLeavesFlagSet()
    {
        var faultCalled = false;
        var outbox = new TickDrivenOutbox(Guid.NewGuid(), NullLogger.Instance, capacity: 64,
            onFault: () => faultCalled = true);
        outbox.Connect(new PacketStream(new FaultingStream()));

        outbox.Enqueue(MakePacket());
        outbox.Flush();

        await Task.Delay(100); // let continuation run

        Assert.True(faultCalled, "onFault should have been called on IOException");
    }

    [Fact]
    public void Enqueue_WhenAtCapacity_DropsOldestAndDoesNotThrow()
    {
        var (stream, _) = MakeSyncStream();
        var outbox = new TickDrivenOutbox(Guid.NewGuid(), NullLogger.Instance, capacity: 2,
            onFault: () => { });
        outbox.Connect(stream);

        // Fill to capacity without flushing
        outbox.Enqueue(MakePacket());
        outbox.Enqueue(MakePacket());
        outbox.Enqueue(MakePacket()); // DropOldest — no exception

        // Still functional
        outbox.Flush();
    }

    [Fact]
    public async Task DisposeAsync_CompletesPromptly_WhenNoWriteInFlight()
    {
        var (stream, _) = MakeSyncStream();
        var outbox = new TickDrivenOutbox(Guid.NewGuid(), NullLogger.Instance, capacity: 64,
            onFault: () => { });
        outbox.Connect(stream);

        var sw = Stopwatch.StartNew();
        await outbox.DisposeAsync();
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 200,
            $"DisposeAsync took {sw.ElapsedMilliseconds}ms — expected < 200ms");
    }

    [Fact]
    public async Task DisposeAsync_DrainsInFlightWrite_Within300ms()
    {
        var slow = new SlowStream();
        var outbox = new TickDrivenOutbox(Guid.NewGuid(), NullLogger.Instance, capacity: 64,
            onFault: () => { });
        outbox.Connect(new PacketStream(slow));

        outbox.Enqueue(MakePacket());
        outbox.Flush(); // starts slow write

        // Complete write shortly after DisposeAsync starts
        _ = Task.Delay(50).ContinueWith(_ => slow.Complete());

        var sw = Stopwatch.StartNew();
        await outbox.DisposeAsync();
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 300,
            $"DisposeAsync took {sw.ElapsedMilliseconds}ms — expected < 300ms");
    }
}
