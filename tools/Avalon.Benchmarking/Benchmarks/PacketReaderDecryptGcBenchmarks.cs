using Avalon.Common.Cryptography;
using Avalon.Configuration;
using Avalon.Hosting.Networking;
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
/// <c>PacketReader.Read(frame, decrypt)</c> call that rents an <c>ArrayPool&lt;byte&gt;</c>
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
///
/// <para>
/// <c>Session_MethodGroupPerPacket</c> and <c>Session_CachedDelegate</c> open a really sealed packet with a real
/// <c>AvalonCryptoSession</c>, the way <c>Connection</c>'s read loop does: before #854 it passed the method group
/// <c>CryptoSession.Decrypt</c> on every packet, a new delegate each time; since, a delegate it created once.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class PacketReaderDecryptGcBenchmarks
{
    private PacketReader _reader = null!;
    private byte[] _originalPayload = null!;
    private NetworkPacketHeader _header;
    // Behind the interface, as Connection holds it.
    private IPacketReader _connectionReader = null!;
    private byte[] _sealedPayload = null!;
    private IAvalonCryptoSession _server = null!;
    private DecryptFunc _cachedDecrypt = null!;

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

        _header = new NetworkPacketHeader { Type = CChatMessagePacket.PacketType };

        // Both ends of one exchange: the client seals, the server opens, as on a live connection.
        var clientKeys = new CryptoManager();
        var serverKeys = new CryptoManager();
        var client = new AvalonCryptoSession(CryptoRole.Client, clientKeys.GetKeyPair());
        var server = new AvalonCryptoSession(CryptoRole.Server, serverKeys.GetKeyPair());
        client.Initialize(serverKeys.GetPublicKey());
        server.Initialize(clientKeys.GetPublicKey());

        _sealedPayload = client.Encrypt(_originalPayload);
        _server = server;
        _connectionReader = _reader;
        _cachedDecrypt = server.Decrypt;
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
        byte[] copy = _originalPayload.ToArray();   // allocates a new byte[] every call
        var frame = new InboundPacketFrame(_header, copy.AsMemory());
        return _reader.Read(frame);
    }

    // -----------------------------------------------------------------------
    // Fixed: Read(frame, decrypt) — rented buffer, no payload swap
    //
    // At steady state the ArrayPool bucket for this size is pre-warmed;
    // Rent/Return is O(1) with no GC allocation.
    // -----------------------------------------------------------------------

    // Static passthrough: copies input to output buffer, returns byte count
    private static readonly DecryptFunc s_passthrough =
        static (input, output) => { input.CopyTo(output); return input.Length; };

    [Benchmark]
    public object? Fixed_DecryptAndRead()
    {
        var frame = new InboundPacketFrame(_header, _originalPayload.AsMemory());
        return _reader.Read(frame, s_passthrough);
    }

    // -----------------------------------------------------------------------
    // The real session, and the delegate handed to Read (#854)
    // -----------------------------------------------------------------------

    [Benchmark]
    public object? Session_MethodGroupPerPacket()
    {
        var frame = new InboundPacketFrame(_header, _sealedPayload.AsMemory());
        return _connectionReader.Read(frame, _server.Decrypt);
    }

    [Benchmark]
    public object? Session_CachedDelegate()
    {
        var frame = new InboundPacketFrame(_header, _sealedPayload.AsMemory());
        return _connectionReader.Read(frame, _cachedDecrypt);
    }
}
