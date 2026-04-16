using Avalon.Common.Threading;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// Measures the allocation reduction from GC-009: converting <c>WorldPacket</c> from
/// a heap-allocated class to a <c>readonly record struct</c>, and replacing the
/// <c>LinkedList&lt;T&gt;</c>-backed <c>Deque&lt;T&gt;</c> inside <c>LockedQueue&lt;T&gt;</c>
/// with a <c>Queue&lt;T&gt;</c> ring buffer.
///
/// <para>
/// <c>Legacy_ClassQueue</c> reproduces the old allocation pattern inline:
/// one <c>class LegacyWorldPacket</c> instance + one <c>LinkedListNode</c> per packet.
/// <c>Struct_RingBuffer</c> uses the production <c>LockedQueue&lt;BenchWorldPacket&gt;</c>
/// after the fix. Both queues are reused across iterations; the ring buffer reaches
/// steady-state capacity after the first iteration (zero allocation per enqueue).
/// </para>
/// </summary>
[MemoryDiagnoser]
public class WorldPacketQueueGcBenchmarks
{
    // -----------------------------------------------------------------------
    // Legacy types — reproduce old heap-allocation pattern
    // -----------------------------------------------------------------------

    private class LegacyWorldPacket
    {
        public NetworkPacketType Type { get; set; }
        public Packet? Payload { get; set; }
    }

    // -----------------------------------------------------------------------
    // Fixed type — struct stored inline in the Queue<T> ring buffer
    // -----------------------------------------------------------------------

    private readonly record struct BenchWorldPacket(NetworkPacketType Type, Packet? Payload);

    // -----------------------------------------------------------------------
    // Shared state — queues reused across iterations
    // -----------------------------------------------------------------------

    private const int PacketCount = 20;

    private readonly object _legacyLock = new();
    private readonly LinkedList<LegacyWorldPacket> _legacyQueue = new();
    private readonly LockedQueue<BenchWorldPacket> _structQueue = new();

    // Static predicates — no closure allocation per call
    private static readonly Func<LegacyWorldPacket, bool> s_legacyPredicate = _ => true;
    private static readonly Func<BenchWorldPacket, bool> s_structPredicate = _ => true;

    // -----------------------------------------------------------------------
    // Legacy: class WorldPacket + LinkedList-backed LockedQueue
    //
    // Reproduces WorldConnection.OnReceive (before GC-009):
    //   _receiveQueue.Add(new WorldPacket { Type = ..., Payload = ... })
    // Each iteration allocates:
    //   PacketCount × (class LegacyWorldPacket object + LinkedListNode<LegacyWorldPacket>)
    // -----------------------------------------------------------------------

    [Benchmark(Baseline = true)]
    public void Legacy_ClassQueue()
    {
        for (int i = 0; i < PacketCount; i++)
        {
            lock (_legacyLock)
                _legacyQueue.AddLast(new LegacyWorldPacket { Type = NetworkPacketType.CMSG_PONG });
        }

        while (true)
        {
            lock (_legacyLock)
            {
                if (_legacyQueue.Count == 0) break;
                var front = _legacyQueue.First!.Value;
                if (!s_legacyPredicate(front)) break;
                _legacyQueue.RemoveFirst();
            }
        }
    }

    // -----------------------------------------------------------------------
    // Fixed: readonly record struct WorldPacket + Queue<T> ring buffer
    //
    // After the first iteration the ring buffer has capacity >= PacketCount.
    // Subsequent iterations: zero allocation per enqueue/dequeue cycle.
    // -----------------------------------------------------------------------

    [Benchmark]
    public void Struct_RingBuffer()
    {
        for (int i = 0; i < PacketCount; i++)
            _structQueue.Add(new BenchWorldPacket(NetworkPacketType.CMSG_PONG, null));

        while (_structQueue.Next(out BenchWorldPacket _, s_structPredicate)) { }
    }
}
