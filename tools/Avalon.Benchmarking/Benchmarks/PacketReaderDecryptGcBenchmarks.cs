using System.Buffers;
using System.IO;
using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Social;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProtoBuf;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// Measures the allocation reduction from GC-008: replacing the old two-step
/// <c>PacketReader.Decrypt</c> (which allocated a new <c>byte[]</c> for the decrypted
/// payload and swapped <c>packet.Payload</c>) + <c>PacketReader.Read</c> with a single
/// <c>PacketReader.Read(packet, decrypt)</c> call that rents an <c>ArrayPool&lt;byte&gt;</c>
/// buffer, decrypts via spans, and returns the buffer to the pool within the same call.
///
/// <para>
/// <c>Legacy_DecryptAndRead</c> reproduces the old allocation pattern inline:
/// a <c>payload.ToArray()</c> copy simulates the old <c>decryptFunc(packet.Payload)</c>
/// returning a new <c>byte[]</c>, which was then assigned to <c>packet.Payload</c>.
/// <c>Fixed_DecryptAndRead</c> uses the production <c>PacketReader.Read</c> with a
/// passthrough <c>DecryptFunc</c> that writes input into the rented output buffer.
/// Both paths deserialize the same <c>CChatMessagePacket</c> payload (a realistic
/// non-empty encrypted packet, unlike an empty packet whose 0-byte ToArray() allocates nothing).
/// </para>
/// </summary>
[MemoryDiagnoser]
public class PacketReaderDecryptGcBenchmarks
{
    private PacketReader _reader = null!;
    private NetworkPacket _packet = null!;
    private byte[] _originalPayload = null!;

    [GlobalSetup]
    public void Setup()
    {
        _reader = new PacketReader(
            NullLoggerFactory.Instance,
            Options.Create(new HostingConfiguration()),
            [typeof(CChatMessagePacket)]);

        using var ms = new MemoryStream();
        Serializer.Serialize(ms, new CChatMessagePacket { Message = "Hello, world!", DateTime = DateTime.UtcNow });
        _originalPayload = ms.ToArray();

        _packet = new NetworkPacket
        {
            Header = new NetworkPacketHeader { Type = CChatMessagePacket.PacketType },
            Payload = _originalPayload
        };
    }

    // -----------------------------------------------------------------------
    // Legacy: separate Decrypt (new byte[]) + Read
    //
    // Reproduces Connection.ExecuteAsync before GC-008:
    //   _packetReader.Decrypt(packet, CryptoSession.Decrypt);   // packet.Payload = new byte[]
    //   Packet? payload = _packetReader.Read(packet);
    //
    // Each iteration allocates a new byte[] for the "decrypted" payload.
    // -----------------------------------------------------------------------

    [Benchmark(Baseline = true)]
    public object? Legacy_DecryptAndRead()
    {
        // Simulates old: packet.Payload = decryptFunc(packet.Payload)  (new byte[] per call)
        _packet.Payload = _originalPayload.ToArray();   // allocates a new byte[] every call
        return _reader.Read(_packet);
    }

    // -----------------------------------------------------------------------
    // Fixed: Read(packet, decrypt) — rented buffer, no payload swap
    //
    // At steady state the ArrayPool bucket for this size is pre-warmed;
    // Rent/Return is O(1) with no GC allocation.
    // -----------------------------------------------------------------------

    // Static passthrough: copies input to output buffer, returns byte count
    private static readonly DecryptFunc s_passthrough =
        static (input, output) => { input.CopyTo(output); return input.Length; };

    [Benchmark]
    public object? Fixed_DecryptAndRead()
        => _reader.Read(_packet, s_passthrough);
}
