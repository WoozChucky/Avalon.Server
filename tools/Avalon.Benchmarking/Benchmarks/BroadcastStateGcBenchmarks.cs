using System.Buffers;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.State;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// Measures the allocation reduction introduced by GC-002, which replaces the
/// per-entity <c>new byte[bytesWritten]</c> copy pattern in
/// <c>MapInstance.BroadcastStateTo</c> with a single contiguous rented buffer
/// and <c>ReadOnlyMemory&lt;byte&gt;</c> slices.
/// The new path also pre-allocates per-player <c>List&lt;ObjectAdd&gt;</c> /
/// <c>List&lt;ObjectUpdate&gt;</c>, eliminating the per-call list allocation.
/// </summary>
[MemoryDiagnoser]
public class BroadcastStateGcBenchmarks
{
    private static readonly EncryptFunc s_encrypt = static span => span.ToArray();

    // Simulates a serialized entity blob — ~64 bytes for a full character write
    // with GameEntityFields.All via WorldObjectWriter.
    private byte[] _entityBlob = null!;

    [Params(5, 20)]
    public int EntityCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _entityBlob = new byte[64];
        Random.Shared.NextBytes(_entityBlob);
        // Pre-warm the [ThreadStatic] PooledArrayBufferWriter so the one-time
        // thread-local allocation is excluded from measurement.
        SInstanceStateAddPacket.Create([], s_encrypt);
    }

    // -----------------------------------------------------------------------
    // Legacy: new byte[] per entity + new List<ObjectAdd> per call
    // (simulates the pattern in BroadcastStateTo before GC-002)
    // -----------------------------------------------------------------------

    [Benchmark(Baseline = true)]
    public NetworkPacket Legacy_BroadcastState()
    {
        byte[] scratch = ArrayPool<byte>.Shared.Rent(4096);
        List<ObjectAdd> adds = [];                              // new list per call

        for (int i = 0; i < EntityCount; i++)
        {
            _entityBlob.CopyTo(scratch, 0);
            int bytesWritten = _entityBlob.Length;
            byte[] fields = new byte[bytesWritten];            // alloc per entity
            scratch.AsSpan(0, bytesWritten).CopyTo(fields);
            adds.Add(new ObjectAdd { Guid = (ulong)i, Fields = fields });
        }

        ArrayPool<byte>.Shared.Return(scratch);
        return SInstanceStateAddPacket.Create(adds, s_encrypt);
    }
}
