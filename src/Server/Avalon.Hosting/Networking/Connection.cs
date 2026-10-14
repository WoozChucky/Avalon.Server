using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using Avalon.Common.Cryptography;
using Avalon.Configuration;
using Avalon.Hosting.Telemetry;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Avalon.Hosting.Networking;

public interface IConnection
{
    Guid Id { get; }
    Task? ExecuteTask { get; }
    string RemoteEndPoint { get; }

    void Close(bool expected = true);

    /// <summary>
    /// Closes and returns when the teardown has finished, so a caller that is about to stop the
    /// process can wait for the queued packets to go out. Every caller waits for the one teardown,
    /// whoever started it.
    /// </summary>
    Task CloseAsync(bool expected = true);

    /// <summary>Takes the packet's payload reference: the connection writes and releases it, or releases it at once.</summary>
    void Send(OutboundPacket packet);
    Task StartAsync(CancellationToken token = default);
}

public abstract partial class Connection : BackgroundService, IConnection, IConnectionRates
{
    private static partial class ReadLog
    {
        [LoggerMessage(Level = LogLevel.Debug,
            Message = "Closing connection {ConnectionId} from {RemoteEndPoint}: a frame declared {Length} bytes, more than Hosting:MaxInboundFrameSize ({MaxInboundFrameSize})")]
        public static partial void FrameTooLong(ILogger logger, Guid connectionId, string remoteEndPoint, long length,
            int maxInboundFrameSize);

        [LoggerMessage(Level = LogLevel.Debug,
            Message = "Closing connection {ConnectionId} from {RemoteEndPoint}: a frame's length prefix is not a length")]
        public static partial void MalformedFrameLength(ILogger logger, Guid connectionId, string remoteEndPoint);

        // A connection that never completed TLS (#897). No TLS is needed to cause one, so a port scan causes one per
        // port it tries: a line at Debug, of fixed text, and a count.
        [LoggerMessage(Level = LogLevel.Debug,
            Message = "Dropped connection {ConnectionId} from {RemoteEndPoint}: the TLS handshake failed (a peer that does not speak TLS, or one the server could not agree a session with)")]
        public static partial void HandshakeFailed(ILogger logger, Guid connectionId, string remoteEndPoint);

        [LoggerMessage(Level = LogLevel.Debug,
            Message = "Dropped connection {ConnectionId} from {RemoteEndPoint}: the TLS handshake did not finish in time")]
        public static partial void HandshakeTimedOut(ILogger logger, Guid connectionId, string remoteEndPoint);

        [LoggerMessage(Level = LogLevel.Debug,
            Message = "Connection {ConnectionId} from {RemoteEndPoint} was not established: the server is stopping")]
        public static partial void StoppedBeforeEstablished(ILogger logger, Guid connectionId, string remoteEndPoint);

        // The four below are what a peer can make of a frame the stream did read (#897). Any peer that completes TLS
        // can, before admission or login, and can reconnect to do it again: each is one line per connection, at
        // Warning, of fixed text, with no exception and nothing the peer wrote but its opcode. Each says how many
        // packets the connection had delivered, which tells a first frame (a peer that is no client, or a client that
        // derives another key) from a session that worked and then received a bad one, and the account, once there
        // is one.
        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Closing connection {ConnectionId} of account {AccountId} from {RemoteEndPoint} after {PacketsReceived} packets: a frame is not a packet (its header, or a length in it, does not parse)")]
        public static partial void MalformedFrame(ILogger logger, Guid connectionId, long? accountId,
            string remoteEndPoint, int packetsReceived);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Closing connection {ConnectionId} of account {AccountId} from {RemoteEndPoint} after {PacketsReceived} packets: {PacketType} arrived sealed while the connection had no session key")]
        public static partial void SealedBeforeKey(ILogger logger, Guid connectionId, long? accountId,
            string remoteEndPoint, int packetsReceived, NetworkPacketType packetType);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Closing connection {ConnectionId} of account {AccountId} from {RemoteEndPoint} after {PacketsReceived} packets: {PacketType} arrived sealed and does not open under the connection's session key")]
        public static partial void SealNotAuthentic(ILogger logger, Guid connectionId, long? accountId,
            string remoteEndPoint, int packetsReceived, NetworkPacketType packetType);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Closing connection {ConnectionId} of account {AccountId} from {RemoteEndPoint} after {PacketsReceived} packets: the payload of {PacketType} does not decode as that packet")]
        public static partial void UndecodablePayload(ILogger logger, Guid connectionId, long? accountId,
            string remoteEndPoint, int packetsReceived, NetworkPacketType packetType);

        // A packet the server does not serve leaves the connection open, so a peer can send them at any rate: the
        // first on a connection is a Warning, the rest Trace. Not Debug: the auth server ships with Debug as its
        // minimum level, and would write one for every frame.
        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Unknown packet type {PacketType} on connection {ConnectionId}: dropped; further packets this server does not serve are logged at Trace on this connection")]
        public static partial void UnknownPacketType(ILogger logger, NetworkPacketType packetType, Guid connectionId);

        [LoggerMessage(Level = LogLevel.Trace,
            Message = "Unknown packet type {PacketType} on connection {ConnectionId}: dropped")]
        public static partial void UnknownPacketTypeRepeated(ILogger logger, NetworkPacketType packetType,
            Guid connectionId);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Could not find a handler for packet {PacketType} on connection {ConnectionId}; further packets this server does not serve are logged at Trace on this connection")]
        public static partial void NoHandler(ILogger logger, NetworkPacketType packetType, Guid connectionId);

        [LoggerMessage(Level = LogLevel.Trace,
            Message = "Could not find a handler for packet {PacketType} on connection {ConnectionId}")]
        public static partial void NoHandlerRepeated(ILogger logger, NetworkPacketType packetType, Guid connectionId);
    }

    /// <summary>The <c>reason</c> a server's <c>network.in.handshake_failures</c> counts a connection under: a fixed set.</summary>
    protected static class HandshakeFailure
    {
        public const string Failed = "failed";
        public const string TimedOut = "timeout";
    }

    /// <summary>The <c>reason</c> a server's <c>network.in.protocol_kicks</c> counts a close under: a fixed set.</summary>
    protected static class ProtocolViolation
    {
        public const string MalformedFrame = "malformed_frame";
        public const string SealedBeforeKey = "sealed_before_key";
        public const string SealNotAuthentic = "seal_not_authentic";
        public const string UndecodablePayload = "undecodable_payload";

        /// <summary>A world's admission refused the client's public key: the right length, and not a P-256 point.</summary>
        public const string BadPublicKey = "bad_public_key";
    }

    protected readonly ILogger _logger;
    private readonly IPacketReader _packetReader;
    private readonly int _maxInboundFrameSize;

    // KeyExchange's session's Decrypt as a delegate, created once. Passing the method group made a new delegate per
    // packet, and the Release JIT did not keep it off the heap (PacketReaderDecryptGcBenchmarks, #854).
    private readonly DecryptFunc _decrypt;

    // The open of a connection with no session (#878): it refuses as a session with no exchange yet refuses, so the
    // reader takes a frame flagged Encrypted as the peer's protocol violation, sealed_before_key, through the one path
    // that already closes a connection for it (#897). Static, so a refused frame allocates no delegate.
    private static readonly DecryptFunc s_noSessionKey = static (_, _) => throw new CryptoSessionNotInitializedException();

    protected readonly IServerBase Server;

    // Whether this connection has had its one Warning for a packet the server does not serve (an opcode it has no
    // packet type for, or one it has no handler for). Read and written only by the connection's read loop, which
    // delivers one packet at a time.
    private bool _warnedUnserved;

    private TcpClient? _client;
    private int _closed; // 0 = open, 1 = closed
    private readonly TaskCompletionSource _closeCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private PacketStream? _stream;

    protected IOutbox? _outbox;

    protected long BytesReceivedCount;
    protected double BytesReceivedRate;
    protected long BytesSentCount;
    protected double BytesSentRate;
    protected int PacketReceivedCount;
    protected double PacketReceivedRate;
    protected int PacketSentCount;
    protected double PacketSentRate;

    /// <param name="logger">The connection's logger.</param>
    /// <param name="server">The server that accepted it.</param>
    /// <param name="packetReader">The server's reader.</param>
    /// <param name="keyExchange">
    /// The connection's own key pair and session, built for this connection and never shared (a
    /// <see cref="ConnectionKeyExchange" /> says why), or null for a connection that runs no key exchange: the server
    /// decides. With one, a payload flagged Encrypted is opened with its session; without, nothing is opened, and such a
    /// frame closes the connection as one sealed while it has no session key.
    /// </param>
    protected Connection(ILogger logger, IServerBase server, IPacketReader packetReader,
        ConnectionKeyExchange? keyExchange)
    {
        _logger = logger;
        _packetReader = packetReader;
        Server = server;
        // A substitute server in tests has none.
        int maxInboundFrameSize = server.MaxInboundFrameSize;
        _maxInboundFrameSize = maxInboundFrameSize > 0 ? maxInboundFrameSize : HostingConfiguration.DefaultMaxInboundFrameSize;

        KeyExchange = keyExchange;
        _decrypt = keyExchange is null ? s_noSessionKey : keyExchange.Session.Decrypt;
        Id = Guid.NewGuid();
        ConnectionRateRegistry.Shared.Track(this);
    }

    public bool IsConnected => _client?.Connected == true;

    /// <summary>
    /// True from the moment <see cref="Close" /> or <see cref="CloseAsync" /> is first called. The
    /// socket, and so <see cref="IsConnected" />, stays up until the outbox has flushed, which can
    /// be several ticks later.
    /// </summary>
    public bool IsClosing => Volatile.Read(ref _closed) == 1;
    public Guid Id { get; }
    public string RemoteEndPoint { get; private set; } = "Unknown";

    /// <summary>
    /// <see cref="RemoteEndPoint" /> without its port, worked out once when the address is settled
    /// rather than per packet: the telemetry tags every packet with it.
    /// </summary>
    public string ClientAddress { get; private set; } = "Unknown";

    /// <summary>
    /// The connection's key pair and session, or null on a connection that runs no key exchange: the one place either is
    /// held. The read loop opens with this session and the outbox seals with it, and each server reads its own view of
    /// the exchange from here, so what a connection opens with, seals with and tells its peer cannot disagree.
    /// </summary>
    public ConnectionKeyExchange? KeyExchange { get; }

    double IConnectionRates.PacketSentRate => PacketSentRate;
    double IConnectionRates.PacketReceivedRate => PacketReceivedRate;
    double IConnectionRates.BytesSentRate => BytesSentRate;
    double IConnectionRates.BytesReceivedRate => BytesReceivedRate;

    public void Close(bool expected = true) => _ = CloseAsync(expected);

    /// <summary>
    /// Probes connect and send nothing (#528): their disconnects are Trace, below the servers' Debug minimum, since a
    /// load balancer or kubelet probe connects every few seconds.
    /// </summary>
    public static LogLevel DisconnectLogLevel(int packetsReceived) =>
        packetsReceived > 0 ? LogLevel.Information : LogLevel.Trace;

    public Task CloseAsync(bool expected = true)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
            _ = CloseCoreAsync(expected);

        // Whoever asked second waits for the teardown the first one started, rather than being
        // told it is already done while the packets are still going out.
        return _closeCompleted.Task;
    }

    /// <summary>
    /// Tears the connection down in order: flush the outbox, drop the socket, then run the
    /// close handler. A caller that sends a packet and closes on the next line expects the
    /// peer to receive it, so the socket must outlive the flush. The flush is bounded by the
    /// outbox itself, so a peer that has stopped reading cannot stall the teardown.
    /// </summary>
    private async Task CloseCoreAsync(bool expected)
    {
        try
        {
            if (_outbox is not null)
                await _outbox.DisposeAsync().ConfigureAwait(false);

            _client?.Close();

            await OnClose(expected).ConfigureAwait(false);

            _logger.Log(DisconnectLogLevel(Volatile.Read(ref PacketReceivedCount)),
                "Connection {ConnectionId} from {RemoteEndPoint} closed ({CloseKind})",
                Id, RemoteEndPoint, expected ? "expected" : "unexpected");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to close connection {Id}", Id);
        }
        finally
        {
            ConnectionRateRegistry.Shared.Untrack(Id);
            _closeCompleted.TrySetResult();
        }
    }

    public virtual void Send(OutboundPacket packet)
    {
        if (_outbox is null)
        {
            packet.Release();
            return;
        }

        // Read before the outbox has it: once queued, another thread may write it and release it.
        int size = SentSize(packet);
        if (!_outbox.Enqueue(packet)) return;
        Interlocked.Add(ref BytesSentCount, size);
        Interlocked.Increment(ref PacketSentCount);
        OnQueued(size);
    }

    /// <summary>
    /// A packet the outbox accepted, with what it puts on the wire (<see cref="SentSize" />), on the sending thread: a
    /// server counts it in its telemetry here, once, and never a refused one.
    /// </summary>
    protected virtual void OnQueued(int size)
    {
    }

    /// <summary>
    /// What a packet puts on the wire, as the byte counters count it (#875): its header and payload, and the seal's nonce
    /// and tag when the packet is flagged Encrypted and the outbox seals (<see cref="IOutbox.Seals" />: always on the auth
    /// server, on a world connection only with Network:PacketEncryption). Read it before the packet is handed on.
    /// </summary>
    protected int SentSize(in OutboundPacket packet) =>
        packet.Size + (_outbox is { Seals: true } && (packet.Header.Flags & NetworkPacketFlags.Encrypted) != 0
            ? PacketEnvelope.SealOverhead
            : 0);

    protected void Init(TcpClient client)
    {
        _client = client;
        RemoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
        ClientAddress = PacketTags.AddressOf(RemoteEndPoint);
    }

    protected virtual IOutbox OnCreateOutbox() =>
        new ChannelOutbox(Id, _logger, Server.SendBufferCapacity, KeyExchange?.Session,
#pragma warning disable MA0045 // the fault callback is synchronous, and it fires from inside the outbox this close then disposes
            onFault: () => Close(false));
#pragma warning restore MA0045

    protected abstract void OnHandshakeFinished();

    protected abstract Task<PacketStream> GetStream(TcpClient client);

    protected abstract Task OnClose(bool expected = true);

    protected abstract ValueTask OnReceive(NetworkPacketHeader header, Packet? payload);

    protected virtual void OnPacketAccounted(NetworkPacketType type, int size) { }

    /// <summary>
    /// A frame read off the socket while the connection was closing and discarded undecoded, with the length it
    /// declared: a server adds it to its bytes received here, and to nothing else.
    /// </summary>
    protected virtual void OnFrameDiscarded(int size) { }

    /// <summary>
    /// The read loop ended on a frame longer than <c>Hosting:MaxInboundFrameSize</c>, or with a malformed length, and
    /// the connection is about to close: a server counts it in its telemetry here, once per connection.
    /// </summary>
    protected virtual void OnFrameRefused() { }

    /// <summary>
    /// The connection never completed its TLS handshake, because of what the peer did or did not send, and is about to
    /// be dropped: a server counts it in its telemetry here, once per connection, under <paramref name="reason" />,
    /// one of <see cref="HandshakeFailure" />'s constants. A handshake cut short by the server stopping is not one.
    /// </summary>
    protected virtual void OnHandshakeFailed(string reason) { }

    /// <summary>
    /// The read loop ended on a frame the peer got wrong (not a packet, sealed while the connection had no session key,
    /// sealed and not authentic, or a payload that is not its packet), and the connection is about to close: a server
    /// counts it in its telemetry here, once per connection, under <paramref name="reason" />, one of
    /// <see cref="ProtocolViolation" />'s constants.
    /// </summary>
    protected virtual void OnProtocolViolation(string reason) { }

    /// <summary>
    /// The account this connection is logged in or admitted as, for the lines that close it; null until there is one,
    /// and on a connection that has none.
    /// </summary>
    protected virtual long? AccountIdForLog => null;

    /// <summary>
    /// A frame whose opcode this server has no packet type for: an opcode a newer server retired, one meant for the
    /// other server, or noise. Dropped, and the connection stays open (an older client may still send what was
    /// retired, #697). The first packet a connection sends that the server does not serve is a Warning and the rest
    /// are Trace, so a peer cannot write a line per frame (#897).
    /// </summary>
    private void DropUnknown(NetworkPacketType type)
    {
        if (_warnedUnserved)
        {
            ReadLog.UnknownPacketTypeRepeated(_logger, type, Id);
            return;
        }

        _warnedUnserved = true;
        ReadLog.UnknownPacketType(_logger, type, Id);
    }

    /// <summary>
    /// A packet the server decoded and has no handler for (<see cref="ServerBase{T}.CallListener" />, which runs on
    /// this connection's read loop): logged as <see cref="DropUnknown" /> logs, under the same once per connection.
    /// </summary>
    internal void LogNoHandler(NetworkPacketType type)
    {
        if (_warnedUnserved)
        {
            ReadLog.NoHandlerRepeated(_logger, type, Id);
            return;
        }

        _warnedUnserved = true;
        ReadLog.NoHandler(_logger, type, Id);
    }

    /// <summary>
    /// The one line and the one count a connection gets for a payload the peer got wrong
    /// (<see cref="PacketReadException" />). The exception is not logged: its text and its stack trace are as long as
    /// the peer cares to make them (nesting past the decoder's depth is tens of kilobytes), and a peer can reconnect.
    /// </summary>
    private void RefusePayload(PacketReadException refusal)
    {
        // The frame that was refused is not among them: a packet is counted once it has been read.
        int packetsReceived = Volatile.Read(ref PacketReceivedCount);
        switch (refusal.Failure)
        {
            case PacketReadFailure.SealedBeforeKey:
                ReadLog.SealedBeforeKey(_logger, Id, AccountIdForLog, RemoteEndPoint, packetsReceived, refusal.PacketType);
                OnProtocolViolation(ProtocolViolation.SealedBeforeKey);
                break;
            case PacketReadFailure.SealNotAuthentic:
                ReadLog.SealNotAuthentic(_logger, Id, AccountIdForLog, RemoteEndPoint, packetsReceived, refusal.PacketType);
                OnProtocolViolation(ProtocolViolation.SealNotAuthentic);
                break;
            default:
                ReadLog.UndecodablePayload(_logger, Id, AccountIdForLog, RemoteEndPoint, packetsReceived, refusal.PacketType);
                OnProtocolViolation(ProtocolViolation.UndecodablePayload);
                break;
        }
    }

    protected abstract long GetServerTime();

    /// <summary>
    /// A trusted proxy prefixes the connection with a PROXY v2 header naming the real client; it is
    /// read here, before <see cref="GetStream" /> starts TLS, and replaces <see cref="RemoteEndPoint" />.
    /// Other peers are direct clients and are never asked for one, so only a trusted proxy can assert
    /// an address. A trusted peer that sends no valid header in time is dropped: accepting it would
    /// let whoever reached the proxy's address speak for any client.
    /// One that closes before sending any byte (a health check) is logged at Debug, the rest at Warning (#528).
    /// </summary>
    /// <returns>False when the connection must be dropped.</returns>
    private async Task<bool> ResolveProxiedClientAsync(TcpClient client, CancellationToken stoppingToken)
    {
        ProxyProtocolPolicy policy = Server.ProxyProtocol;
        if (client.Client.RemoteEndPoint is not IPEndPoint peer || !policy.IsTrusted(peer.Address))
            return true;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(policy.HeaderTimeout);
        try
        {
            // Not owning the socket: GetStream wraps the same socket next, at the byte after the header.
            await using var raw = new NetworkStream(client.Client, ownsSocket: false);
            ProxyHeader header = await ProxyProtocolV2.ReadAsync(raw, timeout.Token).ConfigureAwait(false);
            if (header.Source is not null)
                RemoteEndPoint = header.Source.ToString();
            ClientAddress = PacketTags.AddressOf(RemoteEndPoint);
            return true;
        }
        catch (ProxyHeaderNotSentException)
        {
            // Connected and closed without a byte: a health check or a port scan, not a bad proxy (#528). Trace, like the
            // disconnect that follows: a probe every few seconds would otherwise fill the Debug log.
            _logger.LogTrace("Dropped connection from trusted proxy {Peer}: closed before sending anything", peer);
            return false;
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException or IOException
                                      || (e is OperationCanceledException && !stoppingToken.IsCancellationRequested))
        {
            _logger.LogWarning("Dropped connection from trusted proxy {Peer}: no valid PROXY header ({Reason})",
                peer, e.Message);
            return false;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (_client is null)
            {
                _logger.LogCritical("Cannot execute when client is null");
                return;
            }

            if (!await ResolveProxiedClientAsync(_client, stoppingToken))
                return;

            _logger.LogInformation("New connection from {RemoteEndPoint}", RemoteEndPoint);

            _stream = await GetStream(_client);

            _outbox = OnCreateOutbox();
            _outbox.Connect(_stream);

            OnHandshakeFinished();

            try
            {
                await foreach (ReadOnlyMemory<byte> raw in _stream!.EnumerateRawFramesAsync(_packetReader.BufferSize,
                                   _maxInboundFrameSize, stoppingToken))
                {
                    // Closing (#927): the socket stays up until the outbox has flushed, and the peer may go on sending
                    // until then. Its frames are still read, so the close ends the stream with nothing unread behind
                    // it and the peer is not reset before it has what was sent to it, but they are discarded: not
                    // parsed, not decoded, not handed to OnReceive. Their bytes are bytes received; they are not
                    // packets received. The frame limit above still ends the stream.
                    if (IsClosing)
                    {
                        Interlocked.Add(ref BytesReceivedCount, raw.Length);
                        OnFrameDiscarded(raw.Length);
                        continue;
                    }

                    var frame = InboundPacketFrame.ParseFrame(raw);

                    if (_logger.IsEnabled(LogLevel.Debug) &&
                        frame.Header.Type != NetworkPacketType.CMSG_PONG &&
                        frame.Header.Type != NetworkPacketType.CMSG_PLAYER_INPUT)
                    {
                        _logger.LogDebug("IN: {Type}", frame.Header.Type);
                    }

                    Packet? payload = _packetReader.Read(
                        frame,
                        frame.Header.Flags.HasFlag(NetworkPacketFlags.Encrypted) ? _decrypt : null);

                    int packetSize = frame.Size;
                    Interlocked.Add(ref BytesReceivedCount, packetSize);
                    Interlocked.Increment(ref PacketReceivedCount);
                    OnPacketAccounted(frame.Header.Type, packetSize);

                    // No packet type for the opcode (#897): counted as received, and dropped here. Nothing downstream
                    // is handed a packet that was never decoded.
                    if (payload is null)
                    {
                        DropUnknown(frame.Header.Type);
                        continue;
                    }

                    ValueTask receiveTask = OnReceive(frame.Header, payload);
                    if (!receiveTask.IsCompletedSuccessfully)
                        await receiveTask.ConfigureAwait(false);
                }

                // The stream stopped at a frame it would not read (Hosting:MaxInboundFrameSize): nothing was rented
                // or read for it, and the close below is the one any ended stream gets. Any peer that completes TLS
                // can come here, before admission or login, so it is one Debug line and a count, never an exception.
                if (_stream.FrameRefused)
                {
                    if (_stream.RefusedFrameLength == PacketStream.MalformedLength)
                        ReadLog.MalformedFrameLength(_logger, Id, RemoteEndPoint);
                    else
                        ReadLog.FrameTooLong(_logger, Id, RemoteEndPoint, _stream.RefusedFrameLength, _maxInboundFrameSize);
                    OnFrameRefused();
                }
            }
            // A frame the peer got wrong is a protocol violation, not a server error (#897): one Warning without
            // the exception, one count, and the close below. The two catches cost nothing until a frame is refused,
            // and each ends the loop, so each is once per connection. ParseFrame is the loop's one source of an
            // InvalidDataException: the reader wraps what a payload makes it throw, and a handler's exception ends
            // in CallListener.
            catch (PacketReadException e)
            {
                RefusePayload(e);
            }
            catch (InvalidDataException)
            {
                ReadLog.MalformedFrame(_logger, Id, AccountIdForLog, RemoteEndPoint,
                    Volatile.Read(ref PacketReceivedCount));
                OnProtocolViolation(ProtocolViolation.MalformedFrame);
            }
            catch (IOException e)
            {
                _logger.LogDebug(e, "Connection was closed. Probably by the other party");
            }
            // Anything else is not the peer's doing and keeps its exception: a session whose exchange failed, a
            // cipher or a packet class that threw, a fault in the loop itself.
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to read from stream");
            }
        }
        // ONE CONNECTION'S FAILURE MUST NOT STOP THE SERVER. Everything above the read loop — the TLS
        // handshake most of all — runs unguarded otherwise, and this is a BackgroundService: the host
        // does not configure BackgroundServiceExceptionBehavior, so the .NET default is StopHost and an
        // escaping exception takes the whole process down. A peer that opens a socket and goes away
        // mid-handshake is enough, which makes it reachable by a port scan or a dropped link.
        catch (IOException e)
        {
            _logger.LogDebug(e, "Connection ended before it was established. Probably by the other party");
        }
        // A peer that does not speak TLS, or says nothing until the handshake's timeout, is the same step earlier
        // (#897): an HTTP probe or a port scan was an Error with a stack trace per connection here. One line at
        // Debug without the exception, and a count. Above the read loop only the handshake throws either type: the
        // PROXY header's read handles its own timeout, and the loop below has its own catches.
        catch (AuthenticationException)
        {
            ReadLog.HandshakeFailed(_logger, Id, RemoteEndPoint);
            OnHandshakeFailed(HandshakeFailure.Failed);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            ReadLog.HandshakeTimedOut(_logger, Id, RemoteEndPoint);
            OnHandshakeFailed(HandshakeFailure.TimedOut);
        }
        // Cut short by the server stopping: not the peer's doing, so not counted as its, and not an error.
        catch (OperationCanceledException)
        {
            ReadLog.StoppedBeforeEstablished(_logger, Id, RemoteEndPoint);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to establish connection");
        }
        finally
        {
            Close(false);
        }
    }
}
