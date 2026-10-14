using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Sockets;
using Avalon.Common;
using Avalon.Common.Accounts;
using Avalon.Common.Cryptography;
using Avalon.Common.ValueObjects;
using Avalon.Hosting.Networking;
using Avalon.Hosting.Telemetry;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.World.Testing.Scenarios;

/// <summary>
/// A connection whose send path is production's minus the socket: a packet goes through a real
/// <see cref="WorldConnection" />'s <see cref="Connection.Send" />, which counts it and hands it to the world
/// connection's <c>OnQueued</c> (the <c>network.out.bytes</c> and <c>network.out.packets</c> counters), onto the
/// connection's own real <see cref="ConnectionSender"/> of the scenario's <see cref="NetworkSendScheduler"/>, sealed by
/// the connection's real, initialised session as its send pass frames it, then written through a real
/// <see cref="PacketStream"/> to a stream that only counts bytes (#875, #972). An allocation measured through it
/// therefore includes the send call, the encoder, the cipher and the send pass. Everything a scenario does not drive
/// throws.
/// </summary>
/// <remarks>
/// The world connection is the sending half only: it has no socket (an unconnected <see cref="TcpClient" />), no read
/// loop and no character; its outbox is connected to the counting stream (<c>InitOutboxForTest</c>), as
/// <c>ReceiveQueueFixture</c> connects it. The scheduler's thread never starts: <c>ScenarioWorld.Tick</c> runs its pass
/// inline, on the measured thread, and the counting stream completes every write synchronously, as a socket with room
/// in its buffer does. That stream stands in for production's <c>SslStream</c>, so baselines exclude the TLS record
/// layer. Each connection makes its own key pair, as a world that seals does (<see cref="ConnectionKeyExchange" />), and
/// agrees its session with one client key the process shares, which is fit for cost measurement only.
/// <para>
/// Dispose it (<see cref="ScenarioWorld" /> disposes its own), then run its scheduler's passes once more: disposing
/// starts the world connection's close, the next pass finishes its sender, and the close then untracks it from the
/// process-wide connection rates. Without that pass the close waits out the sender's close budget.
/// </para>
/// </remarks>
public sealed class ScenarioConnection : IWorldConnection, IDisposable
{
    // Key generation is setup cost, not tick cost: one client key pair for the whole process. Each connection runs its
    // own agreement and key derivation over its own server key pair, so no two share cipher state.
    private static readonly Lazy<byte[]> s_clientPublicKey = new(() =>
        AsymmetricCipher.GetPublicKeyBytes(AsymmetricCipher.GetPublicKeyFromKeyPair(
            AsymmetricCipher.GenerateECDHKeyPair(256))));

    // As WorldConnection.ProcessQueue dispatches a queued input: through the packet telemetry, so the allocation gate
    // measures what the tick pays per received packet too (#875). A source and a meter nothing listens to.
    private static readonly PacketDispatchTelemetry s_telemetry =
        new(new ActivitySource("avalon-scenarios"), new Meter("avalon-scenarios"));

    private readonly PacketTags _tags = new(Guid.NewGuid(), "scenario", null, null);

    private readonly WireConnection _wire;
    private readonly CountingStream _stream = new();
    private readonly Action<ScenarioConnection>? _onUpdateMap;

    /// <param name="scheduler">The send threads this connection's sender belongs to; its passes write what is sent.</param>
    /// <param name="sealPayloads">
    /// False frames every packet plain, as a connection of a world that does not seal (<c>Network:PacketEncryption</c>
    /// off) does.
    /// </param>
    public ScenarioConnection(CharacterEntity character, NetworkSendScheduler scheduler,
        Action<ScenarioConnection>? onUpdateMap = null, bool sealPayloads = true)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        Character = character;
        AccountId = character.Data?.AccountId;
        _onUpdateMap = onUpdateMap;

        _wire = new WireConnection(new WireServer(scheduler, sealPayloads));
        _wire.CryptoSession?.Initialize(s_clientPublicKey.Value);
        _wire.InitOutboxForTest(new PacketStream(_stream));
    }

    /// <summary>The bytes the sender wrote to its stream: sealed payloads plus framing.</summary>
    public long BytesWritten => _stream.BytesWritten;

    /// <summary>The packets the connection's outbox accepted, as <see cref="Connection.Send" /> counts them.</summary>
    public int Sent => _wire.PacketsSent;

    public ICharacter? Character { get; set; }

    public (ObjectGuid Npc, DialogueNodeId Node)? CurrentDialogue { get; set; }

    public ulong? CurrentTargetGuid { get; set; }

    public uint LastInputSeq { get; set; }

    public bool IsConnected => true;
    public bool InGame => true;
    public bool InMap => true;

    /// <summary>Production's send: <see cref="Connection.Send" /> on the world connection, with its <c>OnQueued</c>.</summary>
    public void Send(OutboundPacket packet) => _wire.Send(packet);

    /// <summary>
    /// Starts the world connection's close: its sender finishes on the scheduler's next pass, which the caller runs
    /// after this (<see cref="ScenarioWorld" /> runs one after it disposes its connections), and the close then leaves
    /// the process-wide connection rates.
    /// </summary>
    public void Dispose()
    {
        _wire.Close();
        _wire.Dispose();
    }

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
    /// <summary>The character's account, as the connection was admitted with it: the chat lines carry it.</summary>
    public AccountId? AccountId { get; set; }
    public PendingSpawn? PendingSpawn => throw new NotSupportedException();
    public bool SelectInProgress => throw new NotSupportedException();
    public bool LeaveInProgress => throw new NotSupportedException();
    public long SelectStartedTicks => throw new NotSupportedException();
    public bool IsClosing => throw new NotSupportedException();
    public long Latency => throw new NotSupportedException();
    public long RoundTripTime => throw new NotSupportedException();
    public long CurrentPacketArrivedTicks => throw new NotSupportedException();
    /// <summary>The account's locale, which the quest texts are worded in: a world connection's default.</summary>
    public AccountLocale Locale { get; set; } = AccountLocale.enUS;
    /// <summary>A player's, as admitted: the command dispatcher checks it before it runs a command.</summary>
    public AccountAccessLevel AccessLevel => AccountAccessLevel.Player;
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

    /// <summary>
    /// The world connection a scenario connection sends through: production's class with no socket, whose close only
    /// finishes its sender (a scenario connection never leaves the world through it).
    /// </summary>
    private sealed class WireConnection(WireServer server)
        : WorldConnection(server, new TcpClient(), NullLoggerFactory.Instance, new UnusedReader())
    {
        /// <summary>The packets <see cref="Connection.Send" /> counted as accepted.</summary>
        public int PacketsSent => Volatile.Read(ref PacketSentCount);

        protected override Task OnClose(bool expected = true) => Task.CompletedTask;
    }

    /// <summary>What a world connection reads from its server when it is built and sends: the rest throws.</summary>
    private sealed class WireServer(NetworkSendScheduler scheduler, bool packetEncryption) : IWorldServer, IServerBase
    {
        public NetworkSendScheduler SendScheduler { get; } = scheduler;
        public bool PacketEncryption { get; } = packetEncryption;
        public ReceiveQueueLimit ReceiveQueueLimit => ReceiveQueueLimit.Default;
        public PacketDispatchTelemetry PacketTelemetry => PacketDispatchTelemetry.Disabled;
        public ImmutableArray<IWorldConnection> Connections => throw new NotSupportedException();
        public IWorld World => throw new NotSupportedException();
        public Dictionary<NetworkPacketType, IWorldPacketHandler> PacketHandlers => throw new NotSupportedException();
        public IReadOnlyList<IWorldConnection> SessionsOf(AccountId accountId, IWorldConnection except) => throw new NotSupportedException();
        public IReadOnlyList<IWorldConnection> SessionsHoldingCharacter(CharacterId id, IWorldConnection except) => throw new NotSupportedException();
        public Task RemoveConnection(IConnection connection) => throw new NotSupportedException();
        public Task CallListener(IConnection connection, NetworkPacketHeader header, Packet? payload) => throw new NotSupportedException();
        public long ServerTime => throw new NotSupportedException();
        public int SendBufferCapacity => throw new NotSupportedException();
        public void CallConnectionListener(IConnection connection) => throw new NotSupportedException();
    }

    private sealed class UnusedReader : IPacketReader
    {
        public int BufferSize => throw new NotSupportedException();
        public Packet? Read(InboundPacketFrame frame, DecryptFunc? decrypt = null) => throw new NotSupportedException();
    }

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
