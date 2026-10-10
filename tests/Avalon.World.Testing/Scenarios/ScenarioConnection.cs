using System.Diagnostics;
using System.Diagnostics.Metrics;
using Avalon.Common;
using Avalon.Common.Accounts;
using Avalon.Common.Cryptography;
using Avalon.Common.ValueObjects;
using Avalon.Hosting.Networking;
using Avalon.Hosting.Telemetry;
using Avalon.Network.Packets.Abstractions;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Logging.Abstractions;
using Org.BouncyCastle.Crypto;

namespace Avalon.World.Testing.Scenarios;

/// <summary>
/// A connection whose send path is production's minus the socket: packets are sealed by a real, initialised
/// server-role <see cref="AvalonCryptoSession"/> when they are created, queued and framed by a real
/// <see cref="TickDrivenOutbox"/>, and written through a real <see cref="PacketStream"/> to a stream that
/// only counts bytes. An allocation measured through it therefore includes the cipher and the outbox.
/// Everything a scenario does not drive throws.
/// </summary>
/// <remarks>
/// <see cref="Send"/> mirrors <c>WorldConnection.Send</c> and the <c>Connection</c> base it calls, without the
/// telemetry counters (<c>DiagnosticsConfig.World.BytesSent</c>, <c>PacketsSent</c>, <c>PacketsDropped</c>).
/// The counting stream completes every write synchronously, so the outbox's write continuation runs inline on
/// the thread that flushed, as it does in production over a write that completes at once. That stream stands in
/// for production's <c>SslStream</c>, so baselines exclude the TLS record layer, and the session keys are identical
/// across scenario connections (they share one key pair per end), which is fit for cost measurement only.
/// </remarks>
public sealed class ScenarioConnection : IWorldConnection
{
    // Key generation is setup cost, not tick cost: one ECDH pair per end for the whole process. Each
    // connection still runs its own agreement and key derivation, so no two share cipher state.
    private static readonly Lazy<(AsymmetricCipherKeyPair Server, byte[] ClientPublicKey)> s_keys = new(() =>
    {
        AsymmetricCipherKeyPair server = AsymmetricCipher.GenerateECDHKeyPair(256);
        AsymmetricCipherKeyPair client = AsymmetricCipher.GenerateECDHKeyPair(256);
        return (server, AsymmetricCipher.GetPublicKeyBytes(AsymmetricCipher.GetPublicKeyFromKeyPair(client)));
    });

    // As WorldConnection.ProcessQueue dispatches a queued input: through the packet telemetry, so the allocation gate
    // measures what the tick pays per received packet too (#875). A source and a meter nothing listens to.
    private static readonly PacketDispatchTelemetry s_telemetry =
        new(new ActivitySource("avalon-scenarios"), new Meter("avalon-scenarios"));

    private readonly PacketTags _tags = new(Guid.NewGuid(), "scenario", null, null);

    private readonly TickDrivenOutbox _outbox;
    private readonly CountingStream _stream = new();
    private readonly Action<ScenarioConnection>? _onUpdateMap;

    public ScenarioConnection(CharacterEntity character, Action<ScenarioConnection>? onUpdateMap = null)
    {
        Character = character;
        _onUpdateMap = onUpdateMap;

        (AsymmetricCipherKeyPair serverKeys, byte[] clientPublicKey) = s_keys.Value;
        var session = new AvalonCryptoSession(CryptoRole.Server, serverKeys);
        session.Initialize(clientPublicKey);
        CryptoSession = session;

        _outbox = new TickDrivenOutbox(Guid.NewGuid(), NullLogger.Instance, capacity: 100, onFault: () => { });
        _outbox.Connect(new PacketStream(_stream));
    }

    /// <summary>The bytes the outbox wrote to its stream: sealed payloads plus framing.</summary>
    public long BytesWritten => _stream.BytesWritten;

    /// <summary>The packets the outbox accepted.</summary>
    public int Sent { get; private set; }

    public ICharacter? Character { get; set; }

    public (ObjectGuid Npc, DialogueNodeId Node)? CurrentDialogue { get; set; }

    public ulong? CurrentTargetGuid { get; set; }

    public uint LastInputSeq { get; set; }

    public IAvalonCryptoSession CryptoSession { get; }

    public bool IsConnected => true;
    public bool InGame => true;
    public bool InMap => true;

    public void Send(NetworkPacket packet)
    {
        if (!_outbox.Enqueue(packet)) return;
        Sent++;
    }

    public void FlushOutbox() => _outbox.Flush();

    public void UpdateMap()
    {
        if (_onUpdateMap is null)
            return;

        PacketDispatch dispatch = s_telemetry.Begin(NetworkPacketType.CMSG_PLAYER_INPUT, _tags, NullLogger.Instance);
        try
        {
            _onUpdateMap(this);
        }
        finally
        {
            dispatch.Dispose();
        }
    }

    public Guid Id => throw new NotSupportedException();
    public Task? ExecuteTask => throw new NotSupportedException();
    public string RemoteEndPoint => throw new NotSupportedException();
    public ICryptoManager ServerCrypto => throw new NotSupportedException();
    public AccountId? AccountId { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public PendingSpawn? PendingSpawn => throw new NotSupportedException();
    public bool SelectInProgress => throw new NotSupportedException();
    public bool LeaveInProgress => throw new NotSupportedException();
    public long SelectStartedTicks => throw new NotSupportedException();
    public bool IsClosing => throw new NotSupportedException();
    public long Latency => throw new NotSupportedException();
    public long RoundTripTime => throw new NotSupportedException();
    public long CurrentPacketArrivedTicks => throw new NotSupportedException();
    public AccountLocale Locale { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public AccountAccessLevel AccessLevel => throw new NotSupportedException();
    public bool RespawnInFlight { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public void Close(bool expected = true) => throw new NotSupportedException();
    public Task CloseAsync(bool expected = true) => throw new NotSupportedException();
    public Task StartAsync(CancellationToken token = default) => throw new NotSupportedException();
    public void BeginSelect(long nowTicks) => throw new NotSupportedException();
    public bool LoadReportedEarly => throw new NotSupportedException();
    public void NoteLoadReportedEarly() => throw new NotSupportedException();
    public void CancelSelect() => throw new NotSupportedException();
    public void SetPendingSpawn(ICharacter character, IMapInstance instance, long sinceTicks) => throw new NotSupportedException();
    public PendingSpawn? TakePendingSpawn() => throw new NotSupportedException();
    public void SendTimeSyncPing() => throw new NotSupportedException();
    public void RequestInitialTimeSyncPing() => throw new NotSupportedException();
    public bool TakeInitialTimeSyncPingRequest() => throw new NotSupportedException();

    public void OnPongReceived(long lastServerTimestamp, long clientReceivedTimestamp, long clientSentTimestamp, long serverReceivedTicks) =>
        throw new NotSupportedException();

    public void UpdateSession() => throw new NotSupportedException();
    public void FlushContinuations() => throw new NotSupportedException();
    public void EnqueueContinuation<T>(Task<T> task, Action<T> callback) => throw new NotSupportedException();
    public void EnqueueContinuation(Task task, Action callback) => throw new NotSupportedException();

    /// <summary>A write-only stream that discards what it is given and counts it, completing every write at once.</summary>
    private sealed class CountingStream : Stream
    {
        public long BytesWritten { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) => BytesWritten += count;

        public override void Write(ReadOnlySpan<byte> buffer) => BytesWritten += buffer.Length;

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            BytesWritten += count;
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            BytesWritten += buffer.Length;
            return ValueTask.CompletedTask;
        }

        public override void Flush() { }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
