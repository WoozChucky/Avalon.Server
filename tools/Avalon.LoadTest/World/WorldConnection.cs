using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;
using Avalon.Common.Cryptography;
using Avalon.LoadTest.Api;
using Avalon.LoadTest.Wire;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.World;
using Org.BouncyCastle.Crypto;

namespace Avalon.LoadTest.World;

/// <summary>
/// One bot's connection to a world server, as the game client makes it: TCP, TLS pinned to the certificate the join
/// reply names, admission with a join ticket and the client's session key, then the version handshake. A read loop on
/// its own task drains the socket the whole time, so the server's outbox never backs up behind a busy bot: it raises
/// <see cref="Ack"/> for each <c>SMSG_PLAYER_STATE_ACK</c>, answers pings, queues the few packets the entry and leave
/// code waits for on <see cref="Inbound"/>, applies the world-state packets to <see cref="State"/> when the bot keeps
/// one (and with it hands over map transitions and cast refusals), hands a party member's party packets over, and drops
/// everything else without decoding it.
/// </summary>
/// <remarks>
/// Sends are serialised by one lock, and sealing with the session by another (<see cref="Seal{T}"/>): the codec and the
/// frame writer each reuse one buffer, and the frames of two senders must not interleave. Disposing closes the socket,
/// which is what ends the read loop.
/// </remarks>
public sealed class WorldConnection : IAsyncDisposable
{
    /// <summary>How many awaited packets <see cref="Inbound"/> holds before it drops the oldest.</summary>
    private const int InboundCapacity = 1024;

    private readonly Socket _socket;
    private readonly SslStream _stream;
    private readonly FrameWriter _writer;
    private readonly AvalonCryptoSession _session;
    private readonly byte[] _publicKey;
    private readonly Channel<NetworkPacket> _inbound;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Lock _sealLock = new();
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;
    private volatile string? _closeReason;
    private volatile TaskCompletionSource? _worldState;
    private volatile WorldStateTable? _state;

    private WorldConnection(Socket socket, SslStream stream)
    {
        _socket = socket;
        _stream = stream;
        _writer = new FrameWriter(stream);

        // A session reports its own public key only once initialized, but the admission carries it before the
        // server's key is known: it is taken from the pair the session is built with.
        AsymmetricCipherKeyPair keys = AsymmetricCipher.GenerateECDHKeyPair();
        _publicKey = AsymmetricCipher.GetPublicKeyBytes(AsymmetricCipher.GetPublicKeyFromKeyPair(keys));
        _session = new AvalonCryptoSession(CryptoRole.Client, keys);
        Codec = new PacketCodec(_session);

        _inbound = Channel.CreateBounded<NetworkPacket>(new BoundedChannelOptions(InboundCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
        });
        Closed = Task.Run(ReadLoopAsync);
    }

    /// <summary>
    /// The connection's codec. <see cref="PacketCodec.Decode{T}"/> may be called from any thread;
    /// <see cref="PacketCodec.Encrypted{T}"/> only through <see cref="Seal{T}"/>, which serialises it.
    /// </summary>
    public PacketCodec Codec { get; }

    /// <summary>
    /// The packets the entry and leave code waits for (admission, handshake, character list, create, select and leave
    /// results), in arrival order; bounded, dropping the oldest, so the read loop never waits for a reader. Completed
    /// when the read loop ends.
    /// </summary>
    public ChannelReader<NetworkPacket> Inbound => _inbound.Reader;

    /// <summary>
    /// The objects in the character's view, kept from the world-state packets on the read loop; set only for a bot that
    /// looks for targets (a fighter). Null, the default, leaves those packets unread, as every other behaviour wants:
    /// decoding them costs the bot PC per object in view, ten times a second.
    /// </summary>
    /// <remarks>
    /// Set it before the character is selected (and so before <see cref="ArmSpawnSignal"/>): the world describes each
    /// object in full only once, in the add that brings it into view. A table set later misses those adds: a creature
    /// returns only once it moves or is hurt (its update then carries its position and health), and an object that is
    /// never updated is never seen.
    /// </remarks>
    public WorldStateTable? State
    {
        get => _state;
        set => _state = value;
    }

    /// <summary>Each <c>SMSG_PLAYER_STATE_ACK</c>, still sealed, raised on the read loop: a handler must be quick.</summary>
    public event Action<NetworkPacket>? Ack;

    /// <summary>
    /// Each <c>SMSG_MAP_TRANSITION</c>'s result and map, raised on the read loop while <see cref="State"/> is set (left
    /// unread otherwise). A success has cleared the table first: the world sends the new instance's objects only after
    /// the transition, and the old one's only before it, so the table is emptied between the two on this loop, where
    /// they arrive, and none of the new adds is lost.
    /// </summary>
    public event Action<MapTransitionResult, ushort>? MapTransition;

    /// <summary>Each <c>SMSG_ABILITY_NOT_READY</c>'s reason, raised on the read loop while a handler is set (left unread otherwise).</summary>
    public event Action<CastRejectReason>? CastRefused;

    /// <summary>
    /// Each <c>SMSG_PARTY_INVITE</c>, <c>SMSG_PARTY_RESULT</c> and <c>SMSG_PARTY_ROSTER</c>, still sealed, raised on the
    /// read loop while a handler is set (a fighter); left unread otherwise, as every
    /// <c>SMSG_PARTY_MEMBER_STATUS</c> is.
    /// </summary>
    public event Action<NetworkPacket>? PartyPacket;

    /// <summary>
    /// Completes when the read loop ends: successfully when the server closed the connection or it was disposed,
    /// faulted with the read's exception otherwise.
    /// </summary>
    public Task Closed { get; }

    /// <summary>The reason the server gave in an <c>SMSG_DISCONNECT</c>, if it sent one.</summary>
    public DisconnectReason? DisconnectReason { get; private set; }

    /// <summary>
    /// Dials <paramref name="dialHost"/> (or the destination's host) at the destination's port and completes TLS with
    /// the destination's server name as SNI. The certificate is trusted by its pin, the SHA-256 of the complete DER
    /// leaf, not by a chain: an untrusted issuer is tolerated, a wrong name, an expired certificate or a wrong usage is
    /// not, as the game client checks it.
    /// </summary>
    public static async Task<WorldConnection> ConnectAsync(WorldDestination destination, string? dialHost,
        CancellationToken ct)
    {
        byte[] pin = Pin(destination.TlsCertificateSha256);
        string? refusal = null;
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(dialHost ?? destination.Host, destination.Port, ct);
            var stream = new SslStream(new NetworkStream(socket, ownsSocket: true), leaveInnerStreamOpen: false);
            try
            {
                await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = destination.TlsServerName,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    // The pin is the trust anchor: there is no chain whose revocation could be asked about.
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                    RemoteCertificateValidationCallback = (_, certificate, chain, errors) =>
                        (refusal = Refusal(certificate, chain, errors, pin)) is null,
                }, ct);
            }
            catch (AuthenticationException) when (refusal is not null)
            {
                await stream.DisposeAsync();
                throw new WorldRefusedException($"the world's certificate was refused: {refusal}", "tls");
            }
            catch
            {
                await stream.DisposeAsync();
                throw;
            }

            return new WorldConnection(socket, stream);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Sends the join ticket and the client's public key in the clear and waits for the answer. Accepted: the session
    /// is keyed with the server's key, and every later packet is sealed. Otherwise an
    /// <see cref="AdmissionRefusedException"/>, and the server closes the connection.
    /// </summary>
    public async Task AdmitAsync(string joinTicket, CancellationToken ct)
    {
        NetworkPacket reply = await RequestAsync(CGameAdmissionPacket.Create(joinTicket, _publicKey),
            NetworkPacketType.SMSG_GAME_ADMISSION, ct);
        SGameAdmissionPacket admission = Codec.Decode<SGameAdmissionPacket>(reply);
        if (admission.Result != GameAdmissionResult.Accepted)
            throw new AdmissionRefusedException(admission.Result);

        try
        {
            _session.Initialize(admission.PublicKey);
        }
        catch (Exception error)
        {
            throw new WorldRefusedException($"the server's session key is unusable ({error.GetType().Name})", "bad-key");
        }
    }

    /// <summary>Sends the client version and waits for the server to verify it; a refusal is a <see cref="WorldRefusedException"/>.</summary>
    public async Task HandshakeAsync(string version, CancellationToken ct)
    {
        NetworkPacket reply = await RequestAsync(CWorldHandshakePacket.Create(version, _session.Encryptor),
            NetworkPacketType.SMSG_WORLD_HANDSHAKE, ct);
        if (!Codec.Decode<SWorldHandshakePacket>(reply).Verified)
            throw new WorldRefusedException($"the server did not verify client version {version}");
    }

    /// <summary>A message sealed with the session's sending key, ready for <see cref="SendAsync"/>; safe from any thread.</summary>
    public NetworkPacket Seal<T>(T message, NetworkPacketType type) where T : class
    {
        lock (_sealLock)
            return Codec.Encrypted(message, type);
    }

    /// <summary>Writes one frame; frames from concurrent callers are written one after the other, never interleaved.</summary>
    public async ValueTask SendAsync(NetworkPacket packet, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            await _writer.WriteAsync(packet, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Sends a request and waits for its answer of <paramref name="replyType"/>. What <see cref="Inbound"/> still holds
    /// is dropped first: a late answer to an earlier request that timed out must not pass for this one's.
    /// </summary>
    public async Task<NetworkPacket> RequestAsync(NetworkPacket request, NetworkPacketType replyType, CancellationToken ct)
    {
        while (_inbound.Reader.TryRead(out _))
        {
        }

        await SendAsync(request, ct);
        return await ExpectAsync(replyType, ct);
    }

    /// <summary>
    /// A task that completes on the first world-state frame (<c>SMSG_WORLD_STATE_ADD</c>, <c>_UPDATE</c> or
    /// <c>_REMOVE</c>) read after this call: the world sends them only to a character in an instance, so the first is
    /// the sign the selected character has spawned. Only the header is looked at here; the frame is decoded only for
    /// <see cref="State"/>.
    /// </summary>
    public Task ArmSpawnSignal()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _worldState = signal;
        return signal.Task;
    }

    /// <summary>
    /// The next packet of <paramref name="type"/> on <see cref="Inbound"/>; the awaited packets of other types before
    /// it are dropped. A <see cref="WorldClosedException"/> when the connection ends first.
    /// </summary>
    public async Task<NetworkPacket> ExpectAsync(NetworkPacketType type, CancellationToken ct)
    {
        ChannelReader<NetworkPacket> reader = _inbound.Reader;
        while (await reader.WaitToReadAsync(ct))
        {
            while (reader.TryRead(out NetworkPacket? packet))
            {
                if (packet.Header.Type == type) return packet;
            }
        }

        throw new WorldClosedException(_closeReason ?? "the connection closed");
    }

    /// <summary>Closes the socket, which ends the read loop, and waits for the loop to end.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        await _lifetime.CancelAsync();
        _stream.Dispose();
        _socket.Dispose();
        try
        {
            await Closed;
        }
        catch
        {
            // The loop's end after a dispose is not a failure of anything the caller is still waiting on.
        }

        _lifetime.Dispose();
    }

    /// <summary>Whether a packet is one the entry or leave code waits for; every other one is dropped unread.</summary>
    private static bool IsAwaited(NetworkPacketType type) => type is NetworkPacketType.SMSG_GAME_ADMISSION
        or NetworkPacketType.SMSG_WORLD_HANDSHAKE
        or NetworkPacketType.SMSG_CHARACTER_LIST
        or NetworkPacketType.SMSG_CHARACTER_CREATED
        or NetworkPacketType.SMSG_CHARACTER_SELECTED
        or NetworkPacketType.SMSG_CHARACTER_LEAVE_RESULT;

    private async Task ReadLoopAsync()
    {
        var reader = new FrameReader(_stream);
        try
        {
            while (await reader.ReadAsync(_lifetime.Token) is { } packet)
            {
                switch (packet.Header.Type)
                {
                    case NetworkPacketType.SMSG_PLAYER_STATE_ACK:
                        Ack?.Invoke(packet);
                        break;
                    case NetworkPacketType.SMSG_PING:
                        Pong(packet);
                        break;
                    case NetworkPacketType.SMSG_WORLD_STATE_ADD or NetworkPacketType.SMSG_WORLD_STATE_UPDATE
                        or NetworkPacketType.SMSG_WORLD_STATE_REMOVE:
                        // Applied first, so a fighter woken by the spawn signal already finds the first add.
                        _state?.Apply(packet, Codec);
                        if (_worldState is not null) Interlocked.Exchange(ref _worldState, null)?.TrySetResult();
                        break;
                    case NetworkPacketType.SMSG_MAP_TRANSITION:
                        if (_state is { } table)
                        {
                            SMapTransitionPacket transition = Codec.Decode<SMapTransitionPacket>(packet);
                            if (transition.Result == MapTransitionResult.Success) table.Clear();
                            MapTransition?.Invoke(transition.Result, transition.MapId);
                        }

                        break;
                    case NetworkPacketType.SMSG_ABILITY_NOT_READY:
                        if (CastRefused is { } refused) refused(Codec.Decode<SAbilityNotReadyPacket>(packet).Reason);
                        break;
                    case NetworkPacketType.SMSG_PARTY_INVITE or NetworkPacketType.SMSG_PARTY_RESULT
                        or NetworkPacketType.SMSG_PARTY_ROSTER:
                        PartyPacket?.Invoke(packet);
                        break;
                    case NetworkPacketType.SMSG_DISCONNECT:
                        DisconnectReason = Codec.Decode<SDisconnectPacket>(packet).ReasonCode;
                        break;
                    case var type when IsAwaited(type):
                        _inbound.Writer.TryWrite(packet);
                        break;
                }
            }

            _closeReason = DisconnectReason is { } reason
                ? $"the server closed the connection ({reason})"
                : "the server closed the connection";
        }
        catch (Exception error) when (Volatile.Read(ref _disposed) != 0)
        {
            // Disposed: the socket was closed under the read, which is how a bot stops reading.
            _closeReason = $"the connection was closed ({error.GetType().Name})";
        }
        catch (Exception error)
        {
            _closeReason = DisconnectReason is { } reason
                ? $"the connection failed after an SMSG_DISCONNECT ({reason}): {error.Message}"
                : $"the connection failed: {error.Message}";
            throw;
        }
        finally
        {
            _inbound.Writer.TryComplete();
        }
    }

    /// <summary>Answers a time-sync ping as the client does, without holding up the read loop for the send.</summary>
    private void Pong(NetworkPacket packet)
    {
        long received = DateTime.UtcNow.Ticks;
        SPingPacket ping = Codec.Decode<SPingPacket>(packet);
        _ = SendDetachedAsync(CPongPacket.Create(ping.ServerTimestamp, received));
    }

    private async Task SendDetachedAsync(NetworkPacket packet)
    {
        try
        {
            await SendAsync(packet, _lifetime.Token);
        }
        catch (Exception)
        {
            // The connection is closing; its read loop reports why.
        }
    }

    /// <summary>The pin as bytes: the destination names it as 64 hex digits.</summary>
    private static byte[] Pin(string sha256Hex)
    {
        if (sha256Hex.Length != 64 || !sha256Hex.All(Uri.IsHexDigit))
            throw new WorldRefusedException("the destination's certificate pin is not a SHA-256 digest in hex", "bad-pin");

        return Convert.FromHexString(sha256Hex);
    }

    /// <summary>
    /// Null for the exact certificate pinned, with its name, validity and usage intact (of the chain's complaints only
    /// an untrusted or unknown issuer, a self-signed or privately issued certificate, is tolerated); otherwise why it
    /// is refused: the policy errors, the chain status flags, or the presented certificate's (public) digest.
    /// </summary>
    private static string? Refusal(X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors, byte[] pin)
    {
        if (certificate is null) return "the world presented no certificate";
        if ((errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0)
            return $"policy errors {errors & ~SslPolicyErrors.RemoteCertificateChainErrors}";

        byte[] presented = SHA256.HashData(certificate.GetRawCertData());
        if (!CryptographicOperations.FixedTimeEquals(presented, pin))
            return $"the certificate presented (SHA-256 {Convert.ToHexString(presented)}) is not the pinned one";

        if ((errors & SslPolicyErrors.RemoteCertificateChainErrors) == 0) return null;
        if (chain is null) return "the certificate's chain could not be built";

        const X509ChainStatusFlags Tolerated = X509ChainStatusFlags.UntrustedRoot | X509ChainStatusFlags.PartialChain;
        X509ChainStatusFlags refused = X509ChainStatusFlags.NoError;
        foreach (X509ChainStatus status in chain.ChainStatus) refused |= status.Status & ~Tolerated;
        return refused == X509ChainStatusFlags.NoError ? null : $"chain status {refused}";
    }
}

/// <summary>
/// The world refused a step, or the tool refused the world: a version it does not verify, a certificate that is not
/// the pinned one, an unusable session key, a destination the tool cannot pin.
/// </summary>
/// <param name="code">One word for the failure counts: <c>refused</c>, <c>tls</c>, <c>bad-key</c>, <c>bad-pin</c>, or an admission result.</param>
public class WorldRefusedException(string message, string code = "refused") : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>The world refused the admission; it closes the connection.</summary>
public sealed class AdmissionRefusedException(GameAdmissionResult result)
    : WorldRefusedException($"the world refused the admission: {result}", result.ToString())
{
    /// <summary>The world's answer: <c>InvalidRequest</c>, <c>AuthorizationRequired</c>, ...</summary>
    public GameAdmissionResult Result { get; } = result;
}

/// <summary>The connection ended while a step waited for the server's answer.</summary>
public sealed class WorldClosedException(string message) : Exception(message);
