using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Avalon.Common.Cryptography;
using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Org.BouncyCastle.Crypto;

namespace Avalon.Benchmarking.OutboxFlush;

/// <summary>
/// #875: the send path over real sockets, the tick's part against the send threads'. Each mode opens
/// <c>connections</c> loopback connections (the server end behind the world's real <see cref="ConnectionSender" />, of
/// one started <see cref="NetworkSendScheduler" /> with the default thread count; the client end is read and discarded on
/// the thread pool, as a peer would), then ticks at 60 Hz on a thread of the world tick thread's priority: <c>packets</c> movement acks
/// encoded and queued on every connection, then the outbox stage, which only wakes the send threads. The tick's part is
/// timed and its allocations counted on the tick thread; the send threads seal (when the connection seals), frame and
/// write meanwhile, and their busy time comes from their own pass metric. Not a BenchmarkDotNet benchmark: a steady
/// state, timed tick by tick.
/// </summary>
/// <remarks>
/// Modes: <c>memory</c> (the scenario runner's counting stream), <c>tcp</c> (a plain socket), <c>tls</c> (an
/// <see cref="SslStream" /> over the socket, as the world serves). <c>seal</c> runs each with every connection's own
/// initialised session (<c>on</c>), with none so every packet goes plain inside the stream (<c>off</c>), or both.
/// <c>wakes</c> splits the tick's sends into that many slices with a wake-up after each (default 1, the world's tick):
/// above 1 the send threads drain, and give their segments back to the pool, while the tick goes on encoding, as they do
/// when a write completion or an off-tick send wakes them during a tick.
/// <c>receive</c> measures the other direction instead: what the read loop allocates per frame it reads over TLS.
/// <c>slow-tcp</c> and <c>slow-tls</c> stall one connection in twenty (small socket buffers both ends, its reader paused
/// for <see cref="StallTicks" /> ticks after the first <see cref="StallTicks" />) and report what the tick thread
/// allocates before, during and after the stall: a client that stops reading must not make the tick allocate.
///
/// Run: dotnet run -c Release --project tools/Avalon.Benchmarking -- outbox-flush [connections] [packets] [ticks] [modes]
/// (a slow mode needs more than 2 x <see cref="StallTicks" /> ticks)
/// [seal] [wakes]; defaults 50,200,500,1000, 1, 1800, memory,tcp,tls, both, 1.
/// </remarks>
public static class OutboxFlushHarness
{
    private static readonly long s_ticksPerFrame = Stopwatch.Frequency / 60;

    /// <summary>A slow mode's stall: this many ticks of normal reading, then this many with the stalled readers paused.</summary>
    private const int StallTicks = 180;

    /// <summary>A slow mode stalls one connection in this many.</summary>
    private const int StalledOneIn = 20;

    /// <summary>A stalled connection's socket buffers, each end: small, so its writes go pending within a few ticks.</summary>
    private const int StalledBufferBytes = 4096;

    // Key generation is setup cost: one ECDH pair per end for the whole process, as the scenario runner does. Each
    // connection's session still runs its own agreement and key derivation, so no two share cipher state.
    private static readonly Lazy<(AsymmetricCipherKeyPair Server, byte[] ClientPublicKey)> s_keys = new(() =>
    {
        AsymmetricCipherKeyPair server = AsymmetricCipher.GenerateECDHKeyPair(256);
        AsymmetricCipherKeyPair client = AsymmetricCipher.GenerateECDHKeyPair(256);
        return (server, AsymmetricCipher.GetPublicKeyBytes(AsymmetricCipher.GetPublicKeyFromKeyPair(client)));
    });

    public static void Run(string[] args)
    {
        int[] counts = (args.Length > 0 ? args[0] : "50,200,500,1000").Split(',')
            .Select(c => int.Parse(c, CultureInfo.InvariantCulture)).ToArray();
        int packets = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 1;
        int ticks = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 1800;
        string[] modes = args.Length > 3 ? args[3].Split(',') : ["memory", "tcp", "tls"];
        bool[] seals = (args.Length > 4 ? args[4] : "both") switch
        {
            "on" => [true],
            "off" => [false],
            "both" => [true, false],
            var other => throw new ArgumentException($"Unknown seal setting {other}: on, off or both"),
        };
        int wakes = args.Length > 5 ? int.Parse(args[5], CultureInfo.InvariantCulture) : 1;

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{RuntimeEnvironment()} | {new NetworkConfiguration().SendThreads} send threads | {packets} packet(s) per connection per tick, {wakes} wake-up(s) per tick, {ticks} ticks at 60 Hz"));
        Console.WriteLine("mode    conns seal | enqueue us mean   p99 | outbox stage us mean   p99 | send threads us/tick (us/conn) | B/tick on the tick");

        using X509Certificate2 certificate = SelfSigned();
        foreach (string mode in modes)
        {
            if (mode == "receive")
            {
                foreach (int count in counts)
                    RunReceive(count, ticks, certificate).GetAwaiter().GetResult();
                continue;
            }

            foreach (int count in counts)
            {
                foreach (bool seal in seals)
                    RunMode(mode, count, packets, ticks, seal, wakes, certificate).GetAwaiter().GetResult();
            }
        }
    }

    private static async Task RunMode(string mode, int count, int packets, int ticks, bool seal, int wakes,
        X509Certificate2 certificate)
    {
        using var meter = new Meter($"outbox-flush-{Guid.NewGuid()}");
        var busyNanoseconds = new StrongBox<long>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter == meter && instrument.Name == "network.send.pass.duration")
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<double>((_, us, _, _) => Interlocked.Add(ref busyNanoseconds.Value, (long)(us * 1000)));
        listener.Start();

        using var scheduler = new NetworkSendScheduler(new NetworkConfiguration(), NullLoggerFactory.Instance,
            TimeProvider.System, new NetworkSendMetrics(meter));
        scheduler.Start();

        bool slow = mode.StartsWith("slow-", StringComparison.Ordinal);
        ReadGate? gate = slow ? new ReadGate() : null;
        var ends = new List<ConnectionEnds>(count);
        using var stop = new CancellationTokenSource();
        try
        {
            using var listenerSocket = new TcpListener(IPAddress.Loopback, 0);
            listenerSocket.Start(count);
            int port = ((IPEndPoint)listenerSocket.LocalEndpoint).Port;
            for (int i = 0; i < count; i++)
            {
                bool stalled = slow && i % StalledOneIn == 0;
                (Stream server, Stream? client, Task reader) = await OpenStreams(slow ? mode["slow-".Length..] : mode,
                    listenerSocket, port, certificate, stop.Token, gate: stalled ? gate : null);
                ConnectionSender sender = scheduler.CreateSender(Guid.NewGuid(), NullLogger.Instance, seal ? Sealer() : null,
                    close: () => { });
                sender.Connect(new PacketStream(server));
                ends.Add(new ConnectionEnds(sender, server, client, reader));
            }

            // The ticks run on a thread of their own at the world tick thread's priority, which defers its wake-ups as that
            // thread does and whose allocations are the tick's. At normal priority, on Windows, a send thread it wakes can
            // take its core and run its pass inside the outbox stage.
            var tick = new Thread(() => Tick(ends, packets, ticks, wakes, scheduler, mode, count, seal, busyNanoseconds, gate))
            {
                Name = "OutboxFlush.Tick",
                Priority = ThreadPriority.Highest,
            };
            tick.Start();
            tick.Join();
        }
        finally
        {
            await stop.CancelAsync();
            await Task.WhenAll(ends.Select(end => end.DisposeAsync().AsTask()));
            scheduler.Stop(TimeSpan.FromSeconds(5));
        }
    }

    private static void Tick(List<ConnectionEnds> ends, int packets, int ticks, int wakes, NetworkSendScheduler scheduler,
        string mode, int count, bool seal, StrongBox<long> busyNanoseconds, ReadGate? gate)
    {
        NetworkSendScheduler.DeferSignalsOnCurrentThread();
        try
        {
            int warmup = Math.Min(300, ticks);
            double[] enqueueUs = new double[ticks];
            double[] signalUs = new double[ticks];
            long[] bytes = new long[ticks];
            int slice = (ends.Count + wakes - 1) / wakes;
            long busyAtStart = 0;
            long next = Stopwatch.GetTimestamp() + s_ticksPerFrame;
            for (int t = -warmup; t < ticks; t++)
            {
                if (t == 0)
                    busyAtStart = Interlocked.Read(ref busyNanoseconds.Value);
                if (gate is not null && t == StallTicks)
                    gate.Close();
                if (gate is not null && t == 2 * StallTicks)
                    gate.Open();

                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long enqueueTicks = 0;
                long signalTicks = 0;
                for (int from = 0; from < ends.Count; from += slice)
                {
                    long start = Stopwatch.GetTimestamp();
                    int to = Math.Min(from + slice, ends.Count);
                    for (int i = from; i < to; i++)
                    {
                        // A movement ack per packet, about the size the world's acks and state updates are.
                        ConnectionSender sender = ends[i].Sender;
                        for (int p = 0; p < packets; p++)
                            sender.Enqueue(SPlayerStateAckPacket.Create(1, 45.25f, 10.5f, 12.75f, 3.5f, -1.25f, 270, PacketEncoder.Shared));
                    }

                    long enqueued = Stopwatch.GetTimestamp();
                    scheduler.SignalAll(); // the outbox stage
                    long signalled = Stopwatch.GetTimestamp();
                    enqueueTicks += enqueued - start;
                    signalTicks += signalled - enqueued;
                }

                if (t >= 0)
                {
                    enqueueUs[t] = enqueueTicks * 1_000_000d / Stopwatch.Frequency;
                    signalUs[t] = signalTicks * 1_000_000d / Stopwatch.Frequency;
                    bytes[t] = GC.GetAllocatedBytesForCurrentThread() - allocated;
                }

                WaitUntil(next);
                next += s_ticksPerFrame;
                if (Stopwatch.GetTimestamp() - next > s_ticksPerFrame * 4)
                    next = Stopwatch.GetTimestamp() + s_ticksPerFrame;
            }

            // The last tick's passes are counted too: they are its sends.
            WaitUntil(Stopwatch.GetTimestamp() + s_ticksPerFrame);
            double busyUsPerTick = (Interlocked.Read(ref busyNanoseconds.Value) - busyAtStart) / 1000d / ticks;
            double enqueueMean = enqueueUs.Average();
            double signalMean = signalUs.Average();
            Array.Sort(enqueueUs);
            Array.Sort(signalUs);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{mode,-6} {count,6} {(seal ? "on " : "off")}  | {enqueueMean,15:F1} {P(enqueueUs, 0.99),6:F1} | {signalMean,20:F1} {P(signalUs, 0.99),5:F1} | {busyUsPerTick,20:F1} ({busyUsPerTick / count,6:F2}) | {bytes.Average(),18:F0}"));
            if (gate is not null)
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {(count + StalledOneIn - 1) / StalledOneIn} stalled for {StallTicks} ticks | tick B/tick mean (largest tick): before {Window(bytes, 0, StallTicks)} | during {Window(bytes, StallTicks, 2 * StallTicks)} | after {Window(bytes, 2 * StallTicks, ticks)}"));
            }
        }
        finally
        {
            NetworkSendScheduler.ResumeSignalsOnCurrentThread();
        }
    }

    /// <summary>
    /// The other direction, off the tick: every client sends one movement-sized frame per tick, and the server end reads
    /// them through <see cref="PacketStream.EnumerateRawFramesAsync" /> and <see cref="InboundPacketFrame.ParseFrame" />,
    /// as <c>Connection</c>'s read loop does before it decrypts. Reports the bytes the process allocated per frame read,
    /// less what the sending thread allocated.
    /// </summary>
    private static async Task RunReceive(int count, int ticks, X509Certificate2 certificate)
    {
        var pairs = new List<(Stream Server, Stream Client, Task Reader)>(count);
        using var stop = new CancellationTokenSource();
        long frames = 0;
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(count);
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            for (int i = 0; i < count; i++)
            {
                (Stream serverStream, Stream? clientStream, Task drain) = await OpenStreams("tls", listener, port, certificate, stop.Token,
                    drainClient: false);
                await drain;
                var server = new PacketStream(serverStream);
                var reader = Task.Run(async () =>
                {
                    try
                    {
                        await foreach (ReadOnlyMemory<byte> raw in server.EnumerateRawFramesAsync(4096, stop.Token))
                        {
                            InboundPacketFrame.ParseFrame(raw);
                            Interlocked.Increment(ref frames);
                        }
                    }
                    catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException) { }
                }, stop.Token);
                pairs.Add((serverStream, clientStream!, reader));
            }

            // One framed, sealed movement-sized packet: what a client's input is on the wire.
            using var burst = new PooledArrayBufferWriter();
            OutboundPacket packet = SPlayerStateAckPacket.Create(1, 45.25f, 10.5f, 12.75f, 3.5f, -1.25f, 270,
                PacketEncoder.Shared);
            PacketEnvelope.Append(burst, packet, Sealer());
            packet.Release();
            byte[] frame = burst.WrittenSpan.ToArray();

            int warmup = Math.Min(300, ticks);
            long allocatedAtStart = 0, framesAtStart = 0, senderAtStart = 0;
            int gen0 = 0;
            long next = Stopwatch.GetTimestamp() + s_ticksPerFrame;
            for (int t = -warmup; t < ticks; t++)
            {
                if (t == 0)
                {
                    allocatedAtStart = GC.GetTotalAllocatedBytes(precise: true);
                    framesAtStart = Interlocked.Read(ref frames);
                    senderAtStart = GC.GetAllocatedBytesForCurrentThread();
                    gen0 = GC.CollectionCount(0);
                }

                foreach ((Stream _, Stream client, Task _) in pairs)
                    client.Write(frame);

                WaitUntil(next);
                next += s_ticksPerFrame;
            }

            long read = Interlocked.Read(ref frames) - framesAtStart;
            long sender = GC.GetAllocatedBytesForCurrentThread() - senderAtStart;
            long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedAtStart - sender;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"receive {count,6}      | {read:N0} frames read over TLS: {allocated / (double)Math.Max(1, read):F1} B allocated per frame read ({allocated:N0} B), sender thread {sender:N0} B, gen0 {GC.CollectionCount(0) - gen0}"));
        }
        finally
        {
            await stop.CancelAsync();
            foreach ((Stream server, Stream client, Task reader) in pairs)
            {
                await server.DisposeAsync();
                await client.DisposeAsync();
                try { await reader; } catch (Exception e) when (e is OperationCanceledException or IOException) { }
            }
        }
    }

    /// <summary>
    /// One loopback connection: the server end (what the world holds) and, over a socket, the client end with a task
    /// that reads and discards what it is sent, as a peer would (unless <paramref name="drainClient" /> is false).
    /// </summary>
    private static async Task<(Stream Server, Stream? Client, Task Reader)> OpenStreams(string mode, TcpListener listener,
        int port, X509Certificate2 certificate, CancellationToken stop, bool drainClient = true, ReadGate? gate = null)
    {
        if (mode == "memory")
            return (new CountingStream(), null, Task.CompletedTask);

        var client = new TcpClient { NoDelay = true };
        if (gate is not null)
            client.ReceiveBufferSize = StalledBufferBytes; // before the connect, so the window starts small
        Task<TcpClient> accept = listener.AcceptTcpClientAsync(stop).AsTask();
        await client.ConnectAsync(IPAddress.Loopback, port, stop);
        TcpClient server = await accept;
        server.NoDelay = true;
        if (gate is not null)
            server.SendBufferSize = StalledBufferBytes;

        Stream serverStream = new NetworkStream(server.Client, ownsSocket: true);
        Stream clientStream = new NetworkStream(client.Client, ownsSocket: true);
        if (mode == "tls")
        {
            var serverTls = new SslStream(serverStream, leaveInnerStreamOpen: false);
            var clientTls = new SslStream(clientStream, leaveInnerStreamOpen: false, (_, _, _, _) => true);
            Task serverAuth = serverTls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                ClientCertificateRequired = false,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            }, stop);
            await clientTls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "localhost" }, stop);
            await serverAuth;
            serverStream = serverTls;
            clientStream = clientTls;
        }
        else if (mode != "tcp")
        {
            throw new ArgumentException($"Unknown mode {mode}: memory, tcp, tls, slow-tcp, slow-tls or receive");
        }

        Task reader = drainClient ? Task.Run(() => Drain(clientStream, gate, stop), stop) : Task.CompletedTask;
        return (serverStream, clientStream, reader);
    }

    private static async Task Drain(Stream stream, ReadGate? gate, CancellationToken stop)
    {
        byte[] buffer = new byte[64 * 1024];
        try
        {
            do
            {
                if (gate is not null)
                    await gate.WaitOpenAsync().WaitAsync(stop);
            }
            while (await stream.ReadAsync(buffer, stop) > 0);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException) { }
    }

    /// <summary>The mean and the largest of <paramref name="values" /> from <paramref name="from" /> to before <paramref name="to" />.</summary>
    private static string Window(long[] values, int from, int to)
    {
        if (from >= Math.Min(to, values.Length))
            return "n/a";

        ArraySegment<long> window = new(values, from, Math.Min(to, values.Length) - from);
        return string.Create(CultureInfo.InvariantCulture, $"{window.Average():F0} ({window.Max():N0})");
    }

    /// <summary>An initialised server-role session, as an admitted world connection holds: one per connection.</summary>
    private static AvalonCryptoSession Sealer()
    {
        (AsymmetricCipherKeyPair serverKeys, byte[] clientPublicKey) = s_keys.Value;
        var session = new AvalonCryptoSession(CryptoRole.Server, serverKeys);
        session.Initialize(clientPublicKey);
        return session;
    }

    private static void WaitUntil(long deadline)
    {
        long remaining = deadline - Stopwatch.GetTimestamp();
        long sleepTicks = remaining - Stopwatch.Frequency / 500; // sleep to ~2 ms before, then spin
        if (sleepTicks > 0)
#pragma warning disable MA0045
            Thread.Sleep(TimeSpan.FromMilliseconds(sleepTicks * 1000.0 / Stopwatch.Frequency));
#pragma warning restore MA0045
        while (Stopwatch.GetTimestamp() < deadline)
            Thread.SpinWait(64);
    }

    private static double P(double[] sorted, double q) => sorted[Math.Clamp((int)Math.Ceiling(q * sorted.Length) - 1, 0, sorted.Length - 1)];

    private static string RuntimeEnvironment() => string.Create(CultureInfo.InvariantCulture,
        $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription}, .NET {Environment.Version}, {Environment.ProcessorCount} processors, server GC {System.Runtime.GCSettings.IsServerGC}, TieredPGO {Environment.GetEnvironmentVariable("DOTNET_TieredPGO") ?? "default"}");

    private static X509Certificate2 SelfSigned()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
        using X509Certificate2 created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // Exported and loaded again, so the private key is usable by the platform's TLS on every OS.
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pkcs12), null);
    }

    /// <summary>
    /// Pauses the stalled connections' readers. Its one completion source is made up front, so closing and opening it
    /// allocate nothing on the tick that does them.
    /// </summary>
    private sealed class ReadGate
    {
        private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _closed;

        public Task WaitOpenAsync() => _closed ? _opened.Task : Task.CompletedTask;

        public void Close() => _closed = true;

        public void Open()
        {
            _closed = false;
            _opened.TrySetResult();
        }
    }

    private sealed record ConnectionEnds(ConnectionSender Sender, Stream Server, Stream? Client, Task Reader) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            // The send threads are running: the close's last pass is theirs.
            await Sender.DisposeAsync();
            await Server.DisposeAsync();
            if (Client is not null) await Client.DisposeAsync();
            try { await Reader; } catch (Exception e) when (e is OperationCanceledException or IOException) { }
        }
    }

    /// <summary>The scenario runner's stream: discards what it is given, completing every write at once.</summary>
    private sealed class CountingStream : Stream
    {
        public long BytesWritten { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => BytesWritten += count;

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            BytesWritten += buffer.Length;
            return ValueTask.CompletedTask;
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
