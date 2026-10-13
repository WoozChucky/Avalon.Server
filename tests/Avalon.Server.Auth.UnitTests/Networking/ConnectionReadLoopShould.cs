using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Avalon.Common.Cryptography;
using Avalon.Configuration;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Handshake;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.Auth.UnitTests.Networking;

/// <summary>
/// End to end over loopback, through the real setup and read loop both servers' connections run: what it does with a
/// peer that never completes TLS, with a frame it will not read, with a frame the peer got wrong, with a packet the server does not serve, and with a frame that
/// arrives while the connection is closing.
/// </summary>
public class ConnectionReadLoopShould
{
    private const int MaxInboundFrameSize = 1024;
    private static readonly TimeSpan s_wait = TimeSpan.FromSeconds(10);

    // The smallest frame the loop parses: two bytes, an empty header.
    private static readonly byte[] s_frame = [0x02, 0x0A, 0x00];

    private sealed class FakeServer : IServerBase
    {
        public int MaxInboundFrameSize => ConnectionReadLoopShould.MaxInboundFrameSize;
        public Task RemoveConnection(IConnection connection) => Task.CompletedTask;
        public Task CallListener(IConnection connection, NetworkPacketHeader header, Packet? payload) => Task.CompletedTask;
        public long ServerTime => 0;
        public int SendBufferCapacity => 16;
        public void CallConnectionListener(IConnection connection) { }
    }

    /// <summary>An outbox whose close waits for the test, so the connection stays closing with its socket up.</summary>
    private sealed class HeldOutbox : IOutbox
    {
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Seals => false;
        public void Connect(PacketStream stream) { }

        public bool Enqueue(OutboundPacket packet)
        {
            packet.Release();
            return false;
        }

        public ValueTask DisposeAsync() => new(Release.Task);
    }

    /// <summary>
    /// A real server's dispatch with no handler for anything, as the auth server has none for a packet it decodes
    /// and does not serve.
    /// </summary>
    private sealed class HandlerlessServer(ILogger logger) : ServerBase<ProbeConnection>(
        Substitute.For<IPacketManager>(), logger, Substitute.For<IServiceProvider>(),
        Options.Create(new HostingConfiguration { Host = "127.0.0.1", Port = 0 }))
    {
        protected override object GetContextPacket(IConnection connection, object? packet, Type packetType) => null!;
        protected override Task OnStoppingAsync(CancellationToken stoppingToken) => Task.CompletedTask;
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }

    /// <summary>
    /// A connection whose <see cref="OnReceive" /> has no closing check of its own, as the auth server's has none: it
    /// stands for a handler being run.
    /// </summary>
    private sealed class ProbeConnection : Connection
    {
        public readonly TaskCompletionSource Closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource FirstHandled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource SecondSeen = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly HeldOutbox Outbox = new();
        public readonly ConcurrentQueue<string> Violations = new();
        public readonly ConcurrentQueue<string> HandshakeFailures = new();
        public readonly IPacketReader Reader;
        private readonly Func<TcpClient, Task<PacketStream>>? _handshake;

        /// <summary>Where a <see cref="CRegisterPacket" /> is sent for dispatch, as the auth server sends every packet.</summary>
        public IServerBase? Dispatch;
        private int _framesRefused;
        private int _handled;
        private int _bytesDiscarded;

        public ProbeConnection(TcpClient client, ILogger logger, IPacketReader reader,
            Func<TcpClient, Task<PacketStream>>? handshake = null)
            : base(logger, new FakeServer(), reader)
        {
            Reader = reader;
            _handshake = handshake;
            Init(client);
        }

        /// <summary>A reader that decodes nothing and hands back a packet for every frame.</summary>
        public static IPacketReader NewReader()
        {
            IPacketReader reader = Substitute.For<IPacketReader>();
            reader.BufferSize.Returns(256);
            reader.Read(default, null).ReturnsForAnyArgs(new CRequestServerInfoPacket());
            return reader;
        }

        public int FramesRefused => Volatile.Read(ref _framesRefused);
        public int Handled => Volatile.Read(ref _handled);
        public int BytesDiscarded => Volatile.Read(ref _bytesDiscarded);
        public int PacketsReceived => Volatile.Read(ref PacketReceivedCount);

        protected override void OnHandshakeFinished() { }

        protected override Task<PacketStream> GetStream(TcpClient client) =>
            _handshake is not null ? _handshake(client) : Task.FromResult(new PacketStream(new NetworkStream(client.Client)));

        protected override IOutbox OnCreateOutbox() => Outbox;

        protected override Task OnClose(bool expected = true)
        {
            Closed.TrySetResult();
            return Task.CompletedTask;
        }

        protected override void OnFrameRefused() => Interlocked.Increment(ref _framesRefused);

        protected override void OnProtocolViolation(string reason) => Violations.Enqueue(reason);

        protected override void OnHandshakeFailed(string reason) => HandshakeFailures.Enqueue(reason);

        protected override void OnFrameDiscarded(int size)
        {
            Interlocked.Add(ref _bytesDiscarded, size);
            SecondSeen.TrySetResult();
        }

        protected override ValueTask OnReceive(NetworkPacketHeader header, Packet? payload)
        {
            if (Dispatch is not null && payload is CRegisterPacket)
                return new ValueTask(Dispatch.CallListener(this, header, payload));

            if (Interlocked.Increment(ref _handled) == 1)
                FirstHandled.TrySetResult();
            else
                SecondSeen.TrySetResult();
            return ValueTask.CompletedTask;
        }

        protected override long GetServerTime() => 0;
    }

    private static async Task<(ProbeConnection Connection, TcpClient Client)> ConnectAsync(ILogger logger,
        IPacketReader? reader = null, Func<TcpClient, Task<PacketStream>>? handshake = null,
        CancellationToken stopping = default)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var client = new TcpClient();
        Task<TcpClient> accept = listener.AcceptTcpClientAsync();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        var connection = new ProbeConnection(await accept, logger, reader ?? ProbeConnection.NewReader(), handshake);
        await connection.StartAsync(stopping);
        return (connection, client);
    }

    /// <summary>A real TLS handshake as a server, as both servers' connections begin, ended by <paramref name="token" />.</summary>
    private static async Task<PacketStream> TlsHandshakeAsync(TcpClient client, X509Certificate2 certificate,
        CancellationToken token)
    {
        var tls = new SslStream(new NetworkStream(client.Client));
        try
        {
            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate }, token);
            return new PacketStream(tls);
        }
        catch
        {
            await tls.DisposeAsync();
            throw;
        }
    }

    private static X509Certificate2 NewCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=avalon-test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 generated =
            request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        return X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pkcs12), null);
    }

    public enum Handshake
    {
        PeerDoesNotSpeakTls,
        PeerSaysNothingUntilTheTimeout,
        ServerStopsDuringIt,
    }

    /// <summary>
    /// A peer needs no TLS to end a connection during the handshake (#897): one that speaks another protocol (an
    /// HTTP probe, a port scan) or says nothing until the handshake's timeout was an Error with a stack trace per
    /// connection. Each is dropped with nothing above Debug, no exception, and one count under its reason. A handshake
    /// cut short by the server stopping is as quiet, and is not counted as a peer's.
    /// </summary>
    [Theory]
    [InlineData(Handshake.PeerDoesNotSpeakTls, "failed")]
    [InlineData(Handshake.PeerSaysNothingUntilTheTimeout, "timeout")]
    [InlineData(Handshake.ServerStopsDuringIt, null)]
    public async Task Drop_a_peer_that_never_completes_tls_quietly_and_count_it_once(Handshake handshake, string? reason)
    {
        var logger = new CapturingLogger();
        using X509Certificate2 certificate = NewCertificate();
        using var stopping = new CancellationTokenSource();
        // Stands for the handshake's own timeout, as the world's transport has one.
        using var handshakeTimeout = new CancellationTokenSource();

        (ProbeConnection connection, TcpClient client) = await ConnectAsync(logger,
            handshake: accepted => TlsHandshakeAsync(accepted, certificate, handshakeTimeout.Token),
            stopping: stopping.Token);
        using (client) using (connection)
        {
            switch (handshake)
            {
                case Handshake.PeerDoesNotSpeakTls:
                    await client.GetStream().WriteAsync("GET / HTTP/1.1\r\nHost: avalon\r\n\r\n"u8.ToArray());
                    break;
                case Handshake.PeerSaysNothingUntilTheTimeout:
                    await handshakeTimeout.CancelAsync();
                    break;
                default:
                    // The server's stop first, so the handshake ends in a server that is stopping.
                    await stopping.CancelAsync();
                    await handshakeTimeout.CancelAsync();
                    break;
            }

            await connection.Closed.Task.WaitAsync(s_wait);

            string[] counted = reason is null ? [] : [reason];
            Assert.Equal(counted, connection.HandshakeFailures);
            Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Warning);
            Assert.Empty(logger.Exceptions);
            Assert.Empty(connection.Violations);
        }
    }

    /// <summary>
    /// The servers' reader, with decoders for <see cref="CRequestServerInfoPacket" /> and
    /// <see cref="CRegisterPacket" /> and for nothing else, writing to <paramref name="logger" /> as the connection
    /// does.
    /// </summary>
    private static PacketReader RealReader(ILogger logger)
    {
        ILoggerFactory loggers = Substitute.For<ILoggerFactory>();
        loggers.CreateLogger(Arg.Any<string>()).Returns(logger);
        return new PacketReader(loggers, Options.Create(new HostingConfiguration()),
            [typeof(CRequestServerInfoPacket), typeof(CRegisterPacket)]);
    }

    /// <summary>One frame as a client writes it: the length, then the header and the payload.</summary>
    private static byte[] Frame(NetworkPacketType type, NetworkPacketFlags flags, byte[] payload)
    {
        using var wire = new MemoryStream();
        Serializer.SerializeWithLengthPrefix(wire,
            new NetworkPacket { Header = new NetworkPacketHeader { Type = type, Flags = flags }, Payload = payload },
            PrefixStyle.Base128);
        return wire.ToArray();
    }

    private static byte[] Encoded(Packet packet)
    {
        using var payload = new MemoryStream();
        Serializer.Serialize(payload, packet);
        return payload.ToArray();
    }

    /// <summary>
    /// A peer whose frame declares more than <c>Hosting:MaxInboundFrameSize</c>, or whose length prefix is no length,
    /// is closed. Any peer can do this before it is admitted or logged in, so the close is counted once and says one
    /// line at Debug: nothing at Warning or above, and no exception through the read loop's "failed to read" path.
    /// </summary>
    [Theory]
    [InlineData(new byte[] { 0x81, 0x08 })]                               // 1,025: one more than the maximum
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 })]       // six bytes: no length
    public async Task Close_a_peer_whose_frame_is_over_the_maximum_quietly_and_count_it_once(byte[] lengthPrefix)
    {
        var logger = new CapturingLogger();
        (ProbeConnection connection, TcpClient client) = await ConnectAsync(logger);
        using (client) using (connection)
        {
            connection.Outbox.Release.SetResult();
            // Less than the frame declares: before the limit, the read loop waited here for the rest.
            await client.GetStream().WriteAsync((byte[])[.. lengthPrefix, .. new byte[64]]);

            await connection.Closed.Task.WaitAsync(s_wait);

            Assert.Equal(1, connection.FramesRefused);
            Assert.Equal(0, connection.Handled);
            Assert.Equal(1, logger.Count(LogLevel.Debug));
            Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Warning);
        }
    }

    public enum WrongFrame
    {
        NotAPacket,
        SealedBeforeAnyKey,
        SealedUnderAnotherKey,
        SealedAndTooShort,
        PayloadCutShort,
        PayloadNestedPastTheDecodersDepth,
    }

    /// <summary>
    /// A frame the peer got wrong is a protocol violation, not a server error (#897). Any peer that completes TLS can
    /// send one before it is admitted or logged in, and reconnect to send another, so each closes the connection with
    /// one Warning that carries no exception (nesting past the decoder's depth made one of tens of kilobytes), one
    /// count under its reason, and nothing at Error. Every one of these was an Error with a stack trace, but the
    /// payload cut short, which was reported at Debug as the peer having closed the connection.
    /// </summary>
    [Theory]
    [InlineData(WrongFrame.NotAPacket, "malformed_frame")]
    [InlineData(WrongFrame.SealedBeforeAnyKey, "sealed_before_key")]
    [InlineData(WrongFrame.SealedUnderAnotherKey, "seal_not_authentic")]
    [InlineData(WrongFrame.SealedAndTooShort, "seal_not_authentic")]
    [InlineData(WrongFrame.PayloadCutShort, "undecodable_payload")]
    [InlineData(WrongFrame.PayloadNestedPastTheDecodersDepth, "undecodable_payload")]
    public async Task Close_a_peer_whose_frame_is_wrong_with_one_warning_and_no_exception(WrongFrame wrong, string reason)
    {
        var logger = new CapturingLogger();
        (ProbeConnection connection, TcpClient client) = await ConnectAsync(logger, RealReader(logger));
        using (client) using (connection)
        {
            connection.Outbox.Release.SetResult();
            NetworkPacketType known = CRequestServerInfoPacket.PacketType;
            if (wrong is WrongFrame.SealedUnderAnotherKey or WrongFrame.SealedAndTooShort)
                connection.CryptoSession.Initialize(new CryptoManager().GetPublicKey());

            await client.GetStream().WriteAsync(wrong switch
            {
                // A header that declares five bytes with one left.
                WrongFrame.NotAPacket => [0x03, 0x0A, 0x05, 0x01],
                WrongFrame.SealedBeforeAnyKey => Frame(known, NetworkPacketFlags.Encrypted, new byte[40]),
                WrongFrame.SealedUnderAnotherKey => Frame(known, NetworkPacketFlags.Encrypted, new byte[40]),
                // Less than a nonce and a tag.
                WrongFrame.SealedAndTooShort => Frame(known, NetworkPacketFlags.Encrypted, new byte[5]),
                // A string of sixteen bytes, of which one is there.
                WrongFrame.PayloadCutShort => Frame(known, NetworkPacketFlags.ClearText, [0x0A, 0x10, 0x41]),
                // Six hundred groups opened inside one another, in a field the packet does not have.
                _ => Frame(known, NetworkPacketFlags.ClearText, [.. Enumerable.Repeat((byte)0x7B, 600)]),
            });

            await connection.Closed.Task.WaitAsync(s_wait);

            Assert.Equal([reason], connection.Violations);
            Assert.Equal(0, connection.Handled);
            (LogLevel level, _) = Assert.Single(logger.Entries, entry => entry.Level >= LogLevel.Warning);
            Assert.Equal(LogLevel.Warning, level);
            Assert.Empty(logger.Exceptions);
        }
    }

    /// <summary>
    /// A packet the server does not serve leaves the connection open: an older client may still send an opcode a
    /// newer server retired. A peer can therefore send them at any rate, so only the first on a connection is a
    /// Warning, whether the server has no packet type for the opcode or decodes it and has no handler for it; each
    /// was a Warning per frame. The rest are Trace, below the Debug the auth server ships with as its minimum level.
    /// The connection goes on handling what it does serve.
    /// </summary>
    [Fact]
    public async Task Warn_once_for_packets_the_server_does_not_serve_and_keep_the_connection_open()
    {
        var logger = new CapturingLogger();
        (ProbeConnection connection, TcpClient client) = await ConnectAsync(logger, RealReader(logger));
        using var dispatch = new HandlerlessServer(logger);
        using (client) using (connection)
        {
            connection.Dispatch = dispatch;
            byte[] unknown = Frame(NetworkPacketType.CMSG_PING, NetworkPacketFlags.ClearText, []);
            byte[] unhandled = Frame(CRegisterPacket.PacketType, NetworkPacketFlags.ClearText, Encoded(new CRegisterPacket()));
            byte[] served = Frame(CRequestServerInfoPacket.PacketType, NetworkPacketFlags.ClearText,
                Encoded(new CRequestServerInfoPacket { ClientVersion = "1" }));

            await client.GetStream().WriteAsync((byte[])[.. unknown, .. unknown, .. unhandled, .. unhandled, .. unknown, .. served]);
            await connection.FirstHandled.Task.WaitAsync(s_wait);

            Assert.Equal(1, connection.Handled);
            Assert.Equal(6, connection.PacketsReceived);
            Assert.Equal(1, logger.Count(LogLevel.Warning));
            Assert.DoesNotContain(logger.Entries, entry => entry.Level > LogLevel.Warning);
            // The other four it does not serve.
            Assert.Equal(4, logger.Count(LogLevel.Trace));
            Assert.Empty(connection.Violations);
            Assert.False(connection.IsClosing);
        }

        await dispatch.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// What is not the peer's doing stays an Error with its exception. A session whose key exchange failed refuses
    /// to open anything, as it refuses before any exchange, and with the same base type: only the second is
    /// something a peer does by sending a sealed frame early.
    /// </summary>
    [Fact]
    public async Task Keep_the_error_and_its_exception_for_a_failure_that_is_not_the_peers()
    {
        var logger = new CapturingLogger();
        (ProbeConnection connection, TcpClient client) = await ConnectAsync(logger, RealReader(logger));
        using (client) using (connection)
        {
            connection.Outbox.Release.SetResult();
            Assert.ThrowsAny<Exception>(() => connection.CryptoSession.Initialize(new byte[91]));

            await client.GetStream().WriteAsync(
                Frame(CRequestServerInfoPacket.PacketType, NetworkPacketFlags.Encrypted, new byte[40]));
            await connection.Closed.Task.WaitAsync(s_wait);

            (LogLevel level, Exception exception) = Assert.Single(logger.Exceptions);
            Assert.Equal(LogLevel.Error, level);
            Assert.IsType<InvalidOperationException>(exception);
            Assert.Empty(connection.Violations);
        }
    }

    /// <summary>
    /// Once a connection is closing, its socket stays up until what was queued for the peer has gone out, and the
    /// peer may still be sending. A frame that arrives then is read, so the close stays clean, and discarded: it runs
    /// no handler (the auth server's ran, on a connection that could no longer answer), and it is not decoded (the
    /// world dropped it only after decoding it). Its bytes are counted as received; it is not a packet received.
    /// </summary>
    [Fact]
    public async Task Read_and_discard_a_frame_that_arrives_while_closing_without_decoding_or_handling_it()
    {
        (ProbeConnection connection, TcpClient client) = await ConnectAsync(new CapturingLogger());
        using (client) using (connection)
        {
            await client.GetStream().WriteAsync(s_frame);
            await connection.FirstHandled.Task.WaitAsync(s_wait);

            connection.Close();
            await client.GetStream().WriteAsync(s_frame);
            await connection.SecondSeen.Task.WaitAsync(s_wait);

            Assert.Equal(1, connection.Handled);
            connection.Reader.ReceivedWithAnyArgs(1).Read(default, null);
            Assert.Equal(1, connection.PacketsReceived);
            Assert.Equal(s_frame.Length - 1, connection.BytesDiscarded);

            connection.Outbox.Release.SetResult();
            await connection.Closed.Task.WaitAsync(s_wait);
        }
    }
}
