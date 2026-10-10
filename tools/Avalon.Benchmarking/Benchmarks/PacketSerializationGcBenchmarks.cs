using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Social;
using BenchmarkDotNet.Attributes;
using ProtoBuf;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// Measures the allocation reduction introduced by GC-001, which replaced the
/// <c>MemoryStream + ToArray + Func&lt;byte[], byte[]&gt;</c> pattern in outbound S-packet
/// <c>Create()</c> methods with <c>PacketSerializationHelper.Serialize()</c>.
/// The new path uses a <c>[ThreadStatic]</c> pooled <c>IBufferWriter&lt;byte&gt;</c> backed by
/// <c>ArrayPool&lt;byte&gt;.Shared</c>, reducing allocations per call from 3 objects
/// (MemoryStream + byte[] from ToArray + byte[] from encrypt) down to 1
/// (byte[] output from encrypt). Since #875 a server packet is encoded into a pooled segment
/// (<see cref="PacketEncoder" />) and sealed only as it is framed; each pooled call here releases its
/// segment, as the send path does once the packet is written.
/// </summary>
[MemoryDiagnoser]
public class PacketSerializationGcBenchmarks
{
    // For the legacy baseline: simulate old Func<byte[], byte[]> — returns the same ref
    // (ToArray() already copied the bytes; no extra alloc needed from the encrypt step)
    private static readonly Func<byte[], byte[]> s_legacyEncrypt = static bytes => bytes;

    // For the new path: an encoder over a pool of its own, each segment released back to it.
    private static readonly PacketEncoder s_encoder = new(new PayloadSegmentPool());

    private string _characterName = null!;
    private string _message = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Pre-warm the [ThreadStatic] PooledArrayBufferWriter so the one-time
        // thread-local allocation is excluded from measurement.
        SCharacterCreatedPacket.Create(SCharacterCreateResult.Success, s_encoder).Release();

        // Pre-create strings so string allocation does not skew medium-packet measurements.
        _characterName = "Gandalf";
        _message = "Hello, world! This is a benchmark chat message.";
    }

    // -----------------------------------------------------------------------
    // Scenario 1: Small packet (SCharacterCreatedPacket — one enum field)
    // -----------------------------------------------------------------------

    [Benchmark(Baseline = true)]
    public NetworkPacket Legacy_SmallPacket()
    {
        using var ms = new MemoryStream();
        var p = new SCharacterCreatedPacket { Result = SCharacterCreateResult.Success };
        Serializer.Serialize(ms, p);
        byte[] payload = s_legacyEncrypt(ms.ToArray());
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = SCharacterCreatedPacket.PacketType,
                Flags = SCharacterCreatedPacket.Flags,
                Protocol = SCharacterCreatedPacket.Protocol,
                Version = 0
            },
            Payload = payload
        };
    }

    [Benchmark]
    public int Pooled_SmallPacket()
        => Written(SCharacterCreatedPacket.Create(SCharacterCreateResult.Success, s_encoder));

    // -----------------------------------------------------------------------
    // Scenario 2: Medium packet (SChatMessagePacket — two ulongs, two strings, DateTime)
    // -----------------------------------------------------------------------

    [Benchmark]
    public NetworkPacket Legacy_MediumPacket()
    {
        using var ms = new MemoryStream();
        var p = new SChatMessagePacket
        {
            AccountId = 1001UL,
            CharacterId = 2002UL,
            CharacterName = _characterName,
            Message = _message,
            DateTime = DateTime.UtcNow
        };
        Serializer.Serialize(ms, p);
        byte[] payload = s_legacyEncrypt(ms.ToArray());
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = SChatMessagePacket.PacketType,
                Flags = SChatMessagePacket.Flags,
                Protocol = SChatMessagePacket.Protocol,
                Version = 0
            },
            Payload = payload
        };
    }

    [Benchmark]
    public int Pooled_MediumPacket()
        => Written(SChatMessagePacket.Create(1001UL, 2002UL, _characterName, _message, DateTime.UtcNow, s_encoder));

    /// <summary>The payload's length, read before the segment goes back to the pool, as the send path's write does.</summary>
    private static int Written(OutboundPacket packet)
    {
        int length = packet.PayloadLength;
        packet.Release();
        return length;
    }
}
