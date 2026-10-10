using Avalon.Common.Cryptography;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.State;
using BenchmarkDotNet.Attributes;
using Org.BouncyCastle.Crypto;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// #875: what the tick pays to encode a state update into a pooled segment, and what a send thread pays to seal and
/// frame it, per packet, for an update of one entity and of thirty (a crowded town).
/// </summary>
[MemoryDiagnoser]
public class PacketEncoderBenchmarks
{
    private readonly PacketEncoder _encoder = new(new PayloadSegmentPool());
    private readonly PooledArrayBufferWriter _burst = new();
    private IAvalonCryptoSession _session = null!;
    private List<ObjectState> _updates = null!;

    [Params(1, 30)]
    public int Entities;

    [GlobalSetup]
    public void Setup()
    {
        AsymmetricCipherKeyPair client = AsymmetricCipher.GenerateECDHKeyPair(256);
        var session = new AvalonCryptoSession(CryptoRole.Server);
        session.Initialize(AsymmetricCipher.GetPublicKeyBytes(AsymmetricCipher.GetPublicKeyFromKeyPair(client)));
        _session = session;
        _updates = [.. Enumerable.Range(0, Entities).Select(i => new ObjectState
        {
            Guid = 10_000UL + (ulong)i,
            Position = new Vec3 { X = i, Y = 0.5f, Z = -i },
            Velocity = new Vec3 { X = 5f, Y = 0f, Z = -5f },
            Orientation = 90f,
            MoveState = MoveState.Running,
            CurrentHealth = 100,
        })];
    }

    [Benchmark(Description = "Encode a state update into a segment (tick)")]
    public int Encode()
    {
        OutboundPacket packet = SInstanceStateUpdatePacket.Create(_updates, _encoder);
        int length = packet.PayloadLength;
        packet.Release();
        return length;
    }

    [Benchmark(Description = "Encode, seal in place and frame (tick and send thread)")]
    public int EncodeSealFrame()
    {
        _burst.Reset();
        OutboundPacket packet = SInstanceStateUpdatePacket.Create(_updates, _encoder);
        int written = PacketEnvelope.Append(_burst, packet, _session);
        packet.Release();
        return written;
    }

    [Benchmark(Description = "Encode and frame plain (TLS only)")]
    public int EncodeFramePlain()
    {
        _burst.Reset();
        OutboundPacket packet = SInstanceStateUpdatePacket.Create(_updates, _encoder);
        int written = PacketEnvelope.Append(_burst, packet, sealer: null);
        packet.Release();
        return written;
    }
}
