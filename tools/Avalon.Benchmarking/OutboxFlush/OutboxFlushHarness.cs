using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Movement;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Benchmarking.OutboxFlush;

/// <summary>
/// #875: what the tick pays in <c>WorldServer.Update</c>'s outbox stage, <c>FlushOutbox</c> over every connection, with
/// the world's real <see cref="TickDrivenOutbox" /> in front of each kind of stream. The scenario runner writes to a
/// stream that only counts bytes, so the TLS record layer and the socket send are not in its numbers; here they are.
/// Each mode opens <c>connections</c> loopback connections (the server end is what the world holds, the client end is
/// read and discarded on the thread pool, as a peer would), then ticks at 60 Hz: <c>packets</c> movement acks queued on
/// every connection, then every outbox flushed, the flush timed. Not a BenchmarkDotNet benchmark: a steady state,
/// timed tick by tick.
/// </summary>
/// <remarks>
/// Modes: <c>memory</c> (the scenario runner's counting stream), <c>tcp</c> (a plain socket), <c>tls</c> (an
/// <see cref="SslStream" /> over the socket, as the world serves), <c>tls-pool</c> (the same, with each connection's
/// write started on the thread pool instead of the tick: the tick frames the packets and queues the write),
/// <c>tls-parallel</c> (the same streams, the flush split among four thread-pool workers that the tick waits for, so
/// every write is still over when the tick ends). The last two are prototypes for #875's decision: they allocate.
/// <c>receive</c> measures the other direction instead: what the read loop allocates per frame it reads over TLS.
///
/// Run: dotnet run -c Release --project tools/Avalon.Benchmarking -- outbox-flush [connections] [packets] [ticks] [modes]
/// </remarks>
public static class OutboxFlushHarness
{
    private static readonly long s_ticksPerFrame = Stopwatch.Frequency / 60;

    // tls-parallel's workers: the tick splits the flush among them and waits for all of them.
    private const int ParallelWorkers = 4;

    public static void Run(string[] args)
    {
        int connections = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 200;
        int packets = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 1;
        int ticks = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 1800;
        string[] modes = args.Length > 3 ? args[3].Split(',') : ["memory", "tcp", "tls", "tls-pool", "tls-parallel"];

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{RuntimeEnvironment()} | {connections} connections, {packets} packet(s) per connection per tick, {ticks} ticks at 60 Hz"));
        Console.WriteLine("mode       | flush ms mean    p50    p95    p99    max | us/conn mean | B/tick (tick thread) | gen0 gen1 gen2");

        using X509Certificate2 certificate = SelfSigned();
        foreach (string mode in modes)
        {
            if (mode == "receive")
                RunReceive(connections, ticks, certificate).GetAwaiter().GetResult();
            else
                RunMode(mode, connections, packets, ticks, certificate).GetAwaiter().GetResult();
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
                ConnectionEnds ends = await Open("tls", listener, port, certificate, stop.Token);
                await ends.Outbox.DisposeAsync();
                var server = new PacketStream(ends.Server!);
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
                pairs.Add((ends.Server!, ends.Client!, reader));
            }

            // One framed movement-sized packet: what a client's input is on the wire.
            NetworkPacket packet = SPlayerStateAckPacket.Create(1, 45.25f, 10.5f, 12.75f, 3.5f, -1.25f, 270,
                data => RandomNumberGenerator.GetBytes(data.Length + 28));
            using var burst = new Avalon.Network.Packets.Serialization.PooledArrayBufferWriter();
            using var temp = new Avalon.Network.Packets.Serialization.PooledArrayBufferWriter();
            OutboxSerializer.AppendPacket(burst, temp, packet);
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
                $"receive    | {read:N0} frames read over TLS: {allocated / (double)Math.Max(1, read):F1} B allocated per frame read ({allocated:N0} B), sender thread {sender:N0} B, gen0 {GC.CollectionCount(0) - gen0}"));
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

    private static async Task RunMode(string mode, int count, int packets, int ticks, X509Certificate2 certificate)
    {
        var ends = new List<ConnectionEnds>(count);
        using var stop = new CancellationTokenSource();
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(count);
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            for (int i = 0; i < count; i++)
                ends.Add(await Open(mode, listener, port, certificate, stop.Token));

            // One sealed-sized movement ack per packet: about the size the world's acks and state updates are.
            NetworkPacket packet = SPlayerStateAckPacket.Create(1, 45.25f, 10.5f, 12.75f, 3.5f, -1.25f, 270,
                data => RandomNumberGenerator.GetBytes(data.Length + 28));

            int warmup = Math.Min(300, ticks);
            double[] flushMs = new double[ticks];
            long[] bytes = new long[ticks];
            int gen0 = 0, gen1 = 0, gen2 = 0;
            long next = Stopwatch.GetTimestamp() + s_ticksPerFrame;
            for (int t = -warmup; t < ticks; t++)
            {
                if (t == 0)
                {
                    gen0 = GC.CollectionCount(0);
                    gen1 = GC.CollectionCount(1);
                    gen2 = GC.CollectionCount(2);
                }

                foreach (ConnectionEnds end in ends)
                {
                    for (int p = 0; p < packets; p++)
                        end.Outbox.Enqueue(packet);
                }

                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                if (mode == "tls-parallel")
                {
                    Parallel.For(0, ParallelWorkers, w =>
                    {
                        for (int i = w; i < ends.Count; i += ParallelWorkers)
                            ends[i].Outbox.Flush();
                    });
                }
                else
                {
                    foreach (ConnectionEnds end in ends)
                        end.Outbox.Flush();
                }
                long elapsed = Stopwatch.GetTimestamp() - start;
                if (t >= 0)
                {
                    flushMs[t] = elapsed * 1000d / Stopwatch.Frequency;
                    bytes[t] = GC.GetAllocatedBytesForCurrentThread() - allocated;
                }

                WaitUntil(next);
                next += s_ticksPerFrame;
                if (Stopwatch.GetTimestamp() - next > s_ticksPerFrame * 4)
                    next = Stopwatch.GetTimestamp() + s_ticksPerFrame;
            }

            gen0 = GC.CollectionCount(0) - gen0;
            gen1 = GC.CollectionCount(1) - gen1;
            gen2 = GC.CollectionCount(2) - gen2;

            Array.Sort(flushMs);
            double mean = flushMs.Average();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{mode,-10} | {mean,13:F3} {P(flushMs, 0.50),6:F3} {P(flushMs, 0.95),6:F3} {P(flushMs, 0.99),6:F3} {flushMs[^1],6:F3} | {mean * 1000 / count,12:F2} | {bytes.Average(),20:F0} | {gen0,4} {gen1,4} {gen2,4}"));
        }
        finally
        {
            await stop.CancelAsync();
            foreach (ConnectionEnds end in ends)
                await end.DisposeAsync();
        }
    }

    private static async Task<ConnectionEnds> Open(string mode, TcpListener listener, int port, X509Certificate2 certificate,
        CancellationToken stop)
    {
        var outbox = new TickDrivenOutbox(Guid.NewGuid(), NullLogger.Instance, capacity: 100, onFault: () => { });
        if (mode == "memory")
        {
            outbox.Connect(new PacketStream(new CountingStream()));
            return new ConnectionEnds(outbox, null, null, Task.CompletedTask);
        }

        var client = new TcpClient { NoDelay = true };
        Task<TcpClient> accept = listener.AcceptTcpClientAsync(stop).AsTask();
        await client.ConnectAsync(IPAddress.Loopback, port, stop);
        TcpClient server = await accept;
        server.NoDelay = true;

        Stream serverStream = new NetworkStream(server.Client, ownsSocket: true);
        Stream clientStream = new NetworkStream(client.Client, ownsSocket: true);
        if (mode is "tls" or "tls-pool" or "tls-parallel")
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
            throw new ArgumentException($"Unknown mode {mode}: memory, tcp, tls, tls-pool or tls-parallel");
        }

        outbox.Connect(new PacketStream(mode == "tls-pool" ? new ThreadPoolWriteStream(serverStream) : serverStream));
        var reader = Task.Run(() => Drain(clientStream, stop), stop);
        return new ConnectionEnds(outbox, serverStream, clientStream, reader);
    }

    private static async Task Drain(Stream stream, CancellationToken stop)
    {
        byte[] buffer = new byte[64 * 1024];
        try
        {
            while (await stream.ReadAsync(buffer, stop) > 0) { }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException) { }
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
        $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription}, .NET {Environment.Version}, {Environment.ProcessorCount} processors, server GC {System.Runtime.GCSettings.IsServerGC}");

    private static X509Certificate2 SelfSigned()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
        using X509Certificate2 created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // Exported and loaded again, so the private key is usable by the platform's TLS on every OS.
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pkcs12), null);
    }

    private sealed record ConnectionEnds(TickDrivenOutbox Outbox, Stream? Server, Stream? Client, Task Reader) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Outbox.DisposeAsync();
            if (Server is not null) await Server.DisposeAsync();
            if (Client is not null) await Client.DisposeAsync();
            try { await Reader; } catch (Exception e) when (e is OperationCanceledException or IOException) { }
        }
    }

    /// <summary>The prototype of an off-tick write: the inner stream's write is started on the thread pool.</summary>
    private sealed class ThreadPoolWriteStream(Stream inner) : Stream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(Task.Run(async () => await inner.WriteAsync(buffer, cancellationToken), CancellationToken.None));

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
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
