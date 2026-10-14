using System.Diagnostics.Metrics;
using Avalon.Common.Cryptography;
using Avalon.Common.Telemetry;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.Serialization;
using Avalon.World.Testing.Fakes;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Org.BouncyCastle.Crypto;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// What a send costs the tick on a world connection (#883): <c>Connection.Send</c> on an admitted
/// <c>WorldConnection</c> with its real sender, against that sender's <c>Enqueue</c> alone. The difference is what
/// <c>Send</c> adds: the packet's counted size, the connection's two interlocked counters and the world's
/// <c>OnQueued</c>, which adds to the <c>network.out.bytes</c> and <c>network.out.packets</c> counters.
/// </summary>
/// <remarks>
/// Each arm sends as the tick does: four packets to the connection, signals deferred, then the send thread's pass,
/// run on the benchmark's thread into a stream that discards what it is given. The pass (the seal, when the connection
/// seals, and the framing) is the same work in both arms, so it cancels in the difference. The packet is a movement
/// acknowledgement, encoded once; each send takes a reference of its own, as a broadcast's recipients do, so no
/// encode is timed.
/// <para>
/// <c>Metered</c> subscribes a <see cref="MeterListener" /> to the two counters, as a world that exports its metrics
/// has: with no listener a counter's <c>Add</c> is a check and nothing more. The listener here only adds to a field,
/// so it is the least a subscribed counter costs, not what the exporter's aggregation costs.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[MinColumn]
public class ConnectionSendBenchmarks
{
    private const int Passes = 64;
    private const int PacketsPerPass = 4;

    private ReceiveQueueFixture _fixture = null!;
    private ConnectionSender _bareSender = null!;
    private MeterListener? _listener;
    private OutboundPacket _packet;
    private long _metered;

    /// <summary><c>Network:PacketEncryption</c>: the sender seals each packet as its pass frames it.</summary>
    [Params(false, true)]
    public bool Sealed { get; set; }

    [Params(false, true)]
    public bool Metered { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _fixture = new ReceiveQueueFixture(Stream.Null, packetEncryption: Sealed);
        AsymmetricCipherKeyPair client = AsymmetricCipher.GenerateECDHKeyPair(256);
        byte[] clientKey = AsymmetricCipher.GetPublicKeyBytes(AsymmetricCipher.GetPublicKeyFromKeyPair(client));
        _fixture.Connection.CryptoSession?.Initialize(clientKey);

        // The same sender with no connection in front of it: the fixture's scheduler, the same kind of stream, and a
        // session of its own when it seals.
        IAvalonCryptoSession? sealer = null;
        if (Sealed)
        {
            var session = new AvalonCryptoSession(CryptoRole.Server);
            session.Initialize(clientKey);
            sealer = session;
        }

        _bareSender = _fixture.Scheduler!.CreateSender(Guid.NewGuid(), NullLogger.Instance, sealer, close: static () => { });
        _bareSender.Connect(new PacketStream(Stream.Null));

        if (Metered)
        {
            _listener = new MeterListener();
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument, DiagnosticsConfig.World.BytesSent)
                    || ReferenceEquals(instrument, DiagnosticsConfig.World.PacketsSent))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => _metered += measurement);
            _listener.Start();
        }

        // Flagged Encrypted, as every gameplay packet is: sealed by a connection that seals, plain otherwise.
        _packet = SPlayerStateAckPacket.Create(7, 1, 2, 3, 0, 0, 90, PacketEncoder.Shared);
    }

    /// <summary>The sender's enqueue alone, and its pass: what a send cost before the connection counted anything.</summary>
    [Benchmark(Baseline = true, OperationsPerInvoke = Passes * PacketsPerPass)]
    public void SenderEnqueue()
    {
        NetworkSendScheduler.DeferSignalsOnCurrentThread();
        NetworkSendScheduler scheduler = _fixture.Scheduler!;
        for (int pass = 0; pass < Passes; pass++)
        {
            for (int packet = 0; packet < PacketsPerPass; packet++)
                _bareSender.Enqueue(_packet.Share());
            scheduler.RunPass(0);
        }
    }

    /// <summary><c>Connection.Send</c> on the world connection, with its <c>OnQueued</c>, and the same pass.</summary>
    [Benchmark(OperationsPerInvoke = Passes * PacketsPerPass)]
    public void ConnectionSend()
    {
        NetworkSendScheduler.DeferSignalsOnCurrentThread();
        NetworkSendScheduler scheduler = _fixture.Scheduler!;
        ReceiveQueueFixture.ReceiveConnection connection = _fixture.Connection;
        for (int pass = 0; pass < Passes; pass++)
        {
            for (int packet = 0; packet < PacketsPerPass; packet++)
                connection.Send(_packet.Share());
            scheduler.RunPass(0);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        NetworkSendScheduler.ResumeSignalsOnCurrentThread();
        _packet.Release();
        _listener?.Dispose();
        // Finished, like the connection's own sender, by the passes the fixture runs as it is disposed.
        _ = _bareSender.DisposeAsync().AsTask();
        _fixture.Dispose();
    }
}
