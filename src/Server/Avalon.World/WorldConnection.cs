using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Avalon.Common;
using Avalon.Common.Accounts;
using Avalon.Common.Cryptography;
using Avalon.Common.GameAuth;
using Avalon.Common.Telemetry;
using Avalon.Common.ValueObjects;
using Avalon.Hosting.Networking;
using Avalon.Hosting.Telemetry;
using Avalon.Network.Packets.Abstractions;
using Avalon.World.Entities;
using Avalon.World.Filters;
using Avalon.World.GameAuth;
using Avalon.World.Handlers;
using Avalon.World.Maintenance;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Logging;
using Packet = Avalon.Network.Packets.Packet;

namespace Avalon.World;

public partial class WorldConnection : Connection, IWorldConnection, IAccessLevelAssignable, ICharacterLeaveControl,
    ISettledContinuations,
    IMaintenanceBlockable
{
    private readonly ConcurrentQueue<IContinuation> _continuationQueue = new();

    private readonly ConcurrentQueue<WorldPacket> _receiveQueue;

    // Only the tick takes a packet. A head refused by one pass stays here for the other pass,
    // without TryPeek preserving ConcurrentQueue segments and preventing their slot reuse. Tick only, both.
    private WorldPacket _pendingReceivePacket;
    private int _hasPendingReceivePacket;

    // The depth cap (#927). The depth is the difference of two counts, each written by one thread and so never
    // interlocked: the packets the read loop queued, and the packets the tick took out, dispatched or dropped. A held
    // head is still waiting: it is not counted out until it leaves. Both counts wrap; their difference does not.
    private readonly ReceiveQueueLimit _receiveQueueLimit;
    private ReceiveLoopCounts _receiveLoop;
    private uint _handledPackets;

    /// <summary>
    /// The read loop's side of the depth, with 64 bytes of nothing on either side, so it shares a cache line with no
    /// other field wherever the object falls: the read loop writes here for every packet it queues, while the tick
    /// reads and writes the connection's other fields for every packet it takes.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 140)]
    private struct ReceiveLoopCounts
    {
        /// <summary>Packets queued. Written by the read loop; read by the gauge.</summary>
        [FieldOffset(64)] public uint Queued;

        /// <summary>
        /// <see cref="_handledPackets" /> as the read loop last read it: never more than it is now, so the depth
        /// worked out from it is never less than the real one. Read loop only.
        /// </summary>
        [FieldOffset(68)] public uint HandledSeen;

        /// <summary><c>Network:MaxReceiveQueueDepth</c>, here so the check reads no line the tick writes.</summary>
        [FieldOffset(72)] public uint MaxDepth;
    }

    /// <summary>
    /// Packets received and not yet dispatched or dropped, a held head included: what the cap bounds, and what the
    /// receive-queue gauge reads off the tick. The tick's count is read first, so a packet taken between the two
    /// reads counts as still waiting and the difference is never negative.
    /// </summary>
    internal int ReceiveQueueDepth
    {
        get
        {
            uint handled = Volatile.Read(ref _handledPackets);
            return (int)(Volatile.Read(ref _receiveLoop.Queued) - handled);
        }
    }

    private readonly IWorldServer _server;

    // Network:PacketEncryption (#875), the world's, fixed for this connection's life: its sender seals with the session or
    // sends plain inside TLS, and its admission reply names the mode. While it seals, a packet the world runs (the
    // handshake and every session- or map-filter packet but the pong) that arrives plain closes the connection.
    private readonly bool _packetEncryption;

    private CharacterEntity? _characterEntity;

    private long _lastClientTicks;

    // The world's clock (#820): packet arrival stamps, and the admission and heartbeat timings. The time-sync ping is
    // stamped on its send thread, by the send scheduler's clock, as it is written (#875).
    private readonly TimeProvider _time;
    private volatile bool _maintenanceBlocked;

    public WorldConnection(IWorldServer server, TcpClient client, ILoggerFactory loggerFactory,
        IPacketReader packetReader, TimeProvider? time = null)
        : this(server, client, loggerFactory, packetReader, time, new ConnectionKeyExchange())
    {
    }

    private WorldConnection(IWorldServer server, TcpClient client, ILoggerFactory loggerFactory,
        IPacketReader packetReader, TimeProvider? time, ConnectionKeyExchange keyExchange)
        : base(loggerFactory.CreateLogger<WorldConnection>(), (server as IServerBase)!, packetReader, keyExchange)
    {
        CryptoSession = keyExchange.Session;
        ServerCrypto = keyExchange.ServerCrypto;
        _server = server;
        _packetEncryption = server.PacketEncryption;
        // A substitute server in tests has none.
        _receiveQueueLimit = server.ReceiveQueueLimit ?? ReceiveQueueLimit.Default;
        _receiveLoop.MaxDepth = (uint)_receiveQueueLimit.MaxDepth;
        _time = time ?? TimeProvider.System;
        _receiveQueue = new ConcurrentQueue<WorldPacket>();
        _worldSessionFilter = new WorldSessionFilter(this);
        _worldMapFilter = new MapSessionFilter(this);
        _sessionFilterPredicate = wp => _worldSessionFilter.CanProcess(wp.Type);
        _mapFilterPredicate = wp => _worldMapFilter.CanProcess(wp.Type);
        Init(client);
    }

    // Identity is published after access is assigned at exchange. The volatile publication makes
    // the preceding access write visible to the tick that observes a non-null account ID.
    private GameplayWriteAuthority? _gameplayAuthority;
    public GameplayWriteAuthority? GameplayAuthority => Volatile.Read(ref _gameplayAuthority);
    public void BindGameplayAuthority(GameplayWriteAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (authority.GameSessionId == Guid.Empty || authority.FencingToken <= 0 || authority.AccountId.Value <= 0)
            throw new InvalidOperationException("Invalid gameplay admission authority.");
        if (Interlocked.CompareExchange(ref _gameplayAuthority, authority, null) is not null)
            throw new InvalidOperationException("A connection can only be admitted once.");
    }
    private AccountId? _accountId;
    public AccountId? AccountId
    {
        get => Volatile.Read(ref _accountId);
        set
        {
            if (GameplayAuthority is { } authority && value != authority.AccountId)
                throw new InvalidOperationException("An admitted connection cannot change account identity.");
            Volatile.Write(ref _accountId, value);
        }
    }

    /// <summary>Who this connection is, for its packets' spans and log scope.</summary>
    public PacketTags TelemetryTags() =>
        new(Id, ClientAddress, AccountId?.Value, _characterEntity?.Data?.Id.Value);

    public ICharacter? Character
    {
        get => _characterEntity;
        set => _characterEntity = value as CharacterEntity;
    }

    // Written by the character-select handler and its continuations, read by the tick, the
    // readiness barrier and the despawn -- all of which run on the tick thread, as _characterEntity
    // above does.
    private PendingSpawn? _pendingSpawn;

    public PendingSpawn? PendingSpawn => _pendingSpawn;

    /// <inheritdoc />
    // One field behind both members, so the flag and the timestamp cannot disagree about whether a
    // select is in flight.
    private long _selectStartedTicks;

    public bool SelectInProgress => _selectStartedTicks != 0;

    public long SelectStartedTicks => _selectStartedTicks;

    public void BeginSelect(long nowTicks)
    {
        _selectStartedTicks = nowTicks;
        _loadReportedEarly = false;
    }

    // Tick thread only, like the select state above.
    private bool _loadReportedEarly;

    /// <inheritdoc />
    public bool LoadReportedEarly => _loadReportedEarly;

    /// <inheritdoc />
    public void NoteLoadReportedEarly() => _loadReportedEarly = true;

    public void CancelSelect() => _selectStartedTicks = 0;

    // Tick thread only, like the select state above.
    private bool _leaveInProgress;

    /// <inheritdoc />
    public bool LeaveInProgress => _leaveInProgress;

    /// <inheritdoc />
    public bool TryBeginLeave()
    {
        if (_leaveInProgress)
            return false;
        _leaveInProgress = true;
        return true;
    }

    /// <inheritdoc />
    public void EndLeave() => _leaveInProgress = false;

    /// <inheritdoc />
    public void ResetCharacterState()
    {
        LastInputSeq = 0;
        CurrentTargetGuid = null;
        RespawnInFlight = false;
        CurrentDialogue = null;
    }

    public void SetPendingSpawn(ICharacter character, IMapInstance instance, long sinceTicks)
    {
        _pendingSpawn = new PendingSpawn(character, instance, sinceTicks);
        // Cleared here rather than at the call site so the two cannot disagree.
        CancelSelect();
    }

    public PendingSpawn? TakePendingSpawn()
    {
        PendingSpawn? pending = _pendingSpawn;
        _pendingSpawn = null;
        _loadReportedEarly = false;
        return pending;
    }

    // The three below change only when a pong answers a ping this connection wrote (OnPongReceived, #892), and read 0
    // until the first one does.

    /// <summary>The last round trip in whole milliseconds; never negative.</summary>
    public long Latency { get; private set; }

    /// <summary>The last round trip in ticks, measured by the server alone; never negative.</summary>
    public long RoundTripTime { get; private set; }

    /// <summary>Server time minus client time, in ticks, as of the last answered ping.</summary>
    public long TimeSyncOffset { get; private set; }

    /// <inheritdoc />
    public long CurrentPacketArrivedTicks { get; private set; }
    public bool InGame => Character != null;
    // InGame means Character, which is _characterEntity, is set, so the ?. never yields null here.
#pragma warning disable CS8604
    public bool InMap => InGame && _characterEntity?.Map > 0;
#pragma warning restore CS8604

    // Set once per connection by the version handshake's handler, which runs on the connection's read
    // loop (OnReceive hands CMSG_WORLD_HANDSHAKE to CallListener), and taken by the tick, which asks
    // every connection every tick. Two threads, and no interlocking: the handler sets it only after
    // AcceptProtocol succeeded, which it does once per connection (a second handshake closes the
    // connection), so there is one write to see and none to lose; the tick clears it only after reading
    // it set, and a tick that reads it a moment early pings on the next one. A second writer, or a
    // request made more than once, would need an exchange.
    private bool _initialPingRequested;

    /// <inheritdoc />
    public void RequestInitialTimeSyncPing() => _initialPingRequested = true;

    /// <inheritdoc />
    public bool TakeInitialTimeSyncPingRequest()
    {
        if (!_initialPingRequested) return false;
        _initialPingRequested = false;
        return true;
    }

    public void SendTimeSyncPing() => _sender?.EnqueuePing(_lastClientTicks, RoundTripTime, TimeSyncOffset);

    /// <inheritdoc />
    public void OnPongReceived(long lastServerTimestamp, long clientReceivedTimestamp, long clientSentTimestamp,
        long serverReceivedTicks)
    {
        // The echo only says which ping is answered (#892): it must be the stamp the send thread put on a ping it wrote
        // that no pong has answered yet, and it is taken here. A pong that echoes anything else (forged, sent twice, or
        // answering a ping the record has dropped) changes nothing. At Debug, and at most once per ping written: a pong
        // reaches this handler only with a credit (OnReceive).
        if (_sender is null || !_sender.TryTakePingStamp(lastServerTimestamp))
        {
            HighRatePacketLog.PongAnswersNoPing(_logger, NetworkPacketType.CMSG_PONG, Id);
            return;
        }

        // Both ends are the server's own: the stamp it wrote, which the echo now equals, and the pong's arrival. The
        // client's send time plays no part, so the time it took to answer stays in the round trip.
        // serverReceivedTicks rather than the clock: this runs on the world tick, so a pong that landed
        // while the server slept out the rest of a tick would otherwise be charged that whole wait --
        // a round trip of one tick on a loopback connection, and half of it again as a clock offset,
        // because splitting an asymmetric round trip down the middle misattributes the difference.
        long rtt = serverReceivedTicks - lastServerTimestamp;
        if (rtt < 0)
        {
            // The pong arrived before its ping was stamped. Either its echo was sent ahead of the ping (a guess that hit:
            // it came in on the credit of an earlier, unanswered ping), or the wall clock, which both times come from, was
            // set back in between. Nothing was measured, and the stamp is spent.
            HighRatePacketLog.PongBeforeItsPing(_logger, NetworkPacketType.CMSG_PONG, Id);
            return;
        }

        long latency = rtt / TimeSpan.TicksPerMillisecond;

        // Every term from THIS exchange. _lastClientTicks still holds the previous pong's stamp here
        // (it is assigned below), and a later ping can already have gone out before this pong landed --
        // either one turns the offset into the gap between pings. The client's receive time is the one
        // term of its own left: it is its clock, and a false one moves only its own idea of server time.
        TimeSyncOffset = lastServerTimestamp + rtt / 2 - clientReceivedTimestamp;

        if (Latency - latency > 20 && _logger.IsEnabled(LogLevel.Information))
        {
            HighRatePacketLog.LatencyChanged(_logger, Character?.Name ?? AccountId?.ToString(), Latency, latency,
                NetworkPacketType.CMSG_PONG, Id);
        }

        if (_logger.IsEnabled(LogLevel.Trace))
        {
            HighRatePacketLog.RoundTrip(_logger, Character?.Name ?? AccountId?.ToString(), rtt, latency,
                NetworkPacketType.CMSG_PONG, Id);
        }

        _lastClientTicks = clientReceivedTimestamp;
        RoundTripTime = rtt;
        Latency = latency;
    }

    public void UpdateSession()
    {
        ProcessQueue(_sessionFilterPredicate, dropStaleMapPackets: true);
    }

    public void UpdateMap()
    {
        ProcessQueue(_mapFilterPredicate, dropStaleMapPackets: false);
    }

    public void BlockForMaintenance() => _maintenanceBlocked = true;

    public void FlushContinuations()
    {
        ProcessContinuations();
    }

    /// <summary>
    /// The most packets one pass takes out of a connection's receive queue, dispatched or dropped as stale: a pass
    /// that has taken this many stops, and the rest wait for the next. A held head that the pass refuses again is not
    /// one of them. Each tick runs two passes (session, then map), so a connection is taken at most twice this a tick.
    /// </summary>
    public const uint MaxPacketsPerUpdate = 150;

    /// <param name="dropStaleMapPackets">
    ///     The session pass drops, rather than stops at, an in-map packet that reaches the head of the
    ///     queue while the connection holds no character. It was queued while a character was in the
    ///     world, behind a leave (#663) or a despawn, and nothing can accept it any more: left there, it
    ///     would hold back everything behind it, the next character list included.
    /// </param>
    private void ProcessQueue(Func<WorldPacket, bool> predicate, bool dropStaleMapPackets)
    {
        if (!IsGameplayAuthorized)
        {
            if (_hasPendingReceivePacket != 0)
                ReleaseReceivePacket();
            while (_receiveQueue.TryDequeue(out _))
                ReleaseReceivePacket();
            return;
        }

        uint processedPackets = 0;

        // A substitute server in tests has none.
        PacketDispatchTelemetry telemetry = _server.PacketTelemetry ?? PacketDispatchTelemetry.Disabled;

        while (IsConnected)
        {
            WorldPacket packet;
            if (_hasPendingReceivePacket != 0)
                packet = _pendingReceivePacket;
            else if (!_receiveQueue.TryDequeue(out packet))
                break;

            if (!predicate(packet))
            {
                if (!dropStaleMapPackets || Character != null || !MapSessionFilter.IsMapPacket(packet.Type))
                {
                    _pendingReceivePacket = packet;
                    _hasPendingReceivePacket = 1;
                    break;
                }

                ReleaseReceivePacket();
                // Input is kept out of Debug, as where a packet is dropped at arrival: a client sends it 60 times a
                // second, so it is most of what a leave finds queued behind it.
                if (packet.Type == NetworkPacketType.CMSG_PLAYER_INPUT)
                    ReceiveLog.DroppedForCharacterGoneFrequent(_logger, packet.Type);
                else
                    ReceiveLog.DroppedForCharacterGone(_logger, packet.Type);
                if (++processedPackets >= MaxPacketsPerUpdate)
                    break;
                continue;
            }

            ReleaseReceivePacket();
            CurrentPacketArrivedTicks = packet.ArrivedTicks;
            if (_server.PacketHandlers.TryGetValue(packet.Type, out IWorldPacketHandler? handler))
            {
                PacketDispatch dispatch = telemetry.Begin(packet.Type, TelemetryTags(), _logger);
                try
                {
                    handler.Execute(this, packet.Payload!);
                }
                catch (Exception ex)
                {
                    dispatch.Fail(ex);
                    _logger.LogError(ex, "Error processing packet {PacketType} on connection {ConnectionId}", packet.Type, Id);
                }
                finally
                {
                    dispatch.Dispose();
                }
            }
            else
            {
                _logger.LogWarning("No handler for packet {PacketType}", packet.Type);
            }

            if (++processedPackets >= MaxPacketsPerUpdate)
                break;
        }
    }

    /// <summary>
    /// The packet the tick has in hand, from the queue or from the held slot, stops waiting: it is dispatched or
    /// dropped, not held. Empties the slot if the packet came from there, and counts it out of the depth (#927). Every
    /// packet that leaves the receive queue leaves through here, once; a held one is not counted out until it does.
    /// </summary>
    private void ReleaseReceivePacket()
    {
        if (_hasPendingReceivePacket != 0)
        {
            _pendingReceivePacket = default;
            _hasPendingReceivePacket = 0;
        }

        // The tick is the only writer. The gauge reads it, and the read loop when its own figure reaches the cap.
        Volatile.Write(ref _handledPackets, _handledPackets + 1);
    }

    public uint LastInputSeq { get; set; }
    public bool RespawnInFlight { get; set; }
    public ulong? CurrentTargetGuid { get; set; }
    public AccountLocale Locale { get; set; } = AccountLocale.enUS;
    public AccountAccessLevel AccessLevel { get; private set; } = AccountAccessLevel.Player;
    public (ObjectGuid Npc, DialogueNodeId Node)? CurrentDialogue { get; set; }

    public void AssignAccessLevel(AccountAccessLevel level)
    {
        if (GameSessionLease is { } lease && level != lease.AccessLevel)
            throw new InvalidOperationException("An admitted connection cannot change its trusted role.");
        AccessLevel = level;
    }

    protected override void OnQueued(int size)
    {
        DiagnosticsConfig.World.BytesSent.Add(size);
        DiagnosticsConfig.World.PacketsSent.Add(1);
    }

    /// <summary>
    /// A time-sync ping its send thread wrote (#875): encoded there, it never passed <see cref="Connection.Send" />, so it is counted
    /// here, on that thread. Clear text, so its size has no seal overhead.
    /// </summary>
    private void CountPing(int size)
    {
        Interlocked.Add(ref BytesSentCount, size);
        Interlocked.Increment(ref PacketSentCount);
        DiagnosticsConfig.World.BytesSent.Add(size);
        DiagnosticsConfig.World.PacketsSent.Add(1);
    }

    private ConnectionSender? _sender;

    /// <summary>
    /// The connection's sender on its send thread (#875). Its close action runs on that thread or where a write completed:
    /// <see cref="Connection.Close" /> only starts the teardown (the sender's close is awaited off that thread, then the
    /// socket closes), so it neither blocks nor throws there, and a close already started makes it a no-op. It seals with
    /// the session only while the world seals (Network:PacketEncryption); otherwise every packet goes plain inside TLS,
    /// its Encrypted flag cleared.
    /// </summary>
    protected override IOutbox OnCreateOutbox()
    {
        NetworkSendScheduler scheduler = _server.SendScheduler
            ?? throw new InvalidOperationException("The world server has no send scheduler");
#pragma warning disable MA0045 // a slow connection's close starts on its send thread, which must not wait on it
        _sender = scheduler.CreateSender(Id, _logger, _packetEncryption ? CryptoSession : null, close: () => Close(false),
            onPingWritten: CountPing);
#pragma warning restore MA0045
        return _sender;
    }

    /// <summary>Whether this connection's gameplay packets are sealed both ways (Network:PacketEncryption, #875).</summary>
    public bool PacketEncryption => _packetEncryption;

    /// <summary>The connection's session, keyed at admission from the client's public key.</summary>
    public IAvalonCryptoSession CryptoSession { get; }

    /// <summary>The connection's own key pair, whose public key the admission reply sends.</summary>
    public ICryptoManager ServerCrypto { get; }

    public void InitOutboxForTest(PacketStream stream)
    {
        _outbox = OnCreateOutbox();
        _outbox.Connect(stream);
    }

    protected override void OnHandshakeFinished() => Server.CallConnectionListener(this);

    protected override async Task<PacketStream> GetStream(TcpClient client)
    {
        WorldTlsTransport transport = ((WorldServer)Server).TlsTransport;
        SslStream stream = await GameAuth.WorldTlsTransport.AuthenticateAsync(new NetworkStream(client.Client, true), transport.Certificate);
        _transportReadyTicks = _time.GetTimestamp();
        _tlsAuthenticated = true;
        return new PacketStream(stream);
    }

    protected override async Task OnClose(bool expected = true)
    {
        // DeSpawnPlayer mutates MapInstance dictionaries — defer to the tick thread.
        (Server as WorldServer)!.EnqueueDisconnect(this);
        await Server.RemoveConnection(this);
    }

    protected override ValueTask OnReceive(NetworkPacketHeader header, Packet? payload)
    {
        if (_maintenanceBlocked || IsClosing) return ValueTask.CompletedTask;
        if (header.Type == NetworkPacketType.CMSG_GAME_ADMISSION)
            return new ValueTask(Server.CallListener(this, header, payload));
        // Network:PacketEncryption on (#875): a client that follows its admission reply seals every packet the world runs,
        // so a plain one is a protocol violation and closes the connection.
        if (_packetEncryption && (header.Flags & NetworkPacketFlags.Encrypted) == 0 && MustArriveSealed(header.Type))
        {
            ReceiveLog.PlainPacketRefused(_logger, Id, header.Type);
#pragma warning disable MA0045 // the read loop must not wait on the close it starts
            Close(false);
#pragma warning restore MA0045
            return ValueTask.CompletedTask;
        }
        if (header.Type == NetworkPacketType.CMSG_WORLD_HANDSHAKE && GameSessionLease?.IsActive == true)
            return new ValueTask(Server.CallListener(this, header, payload));
        if (!IsGameplayAuthorized) return ValueTask.CompletedTask;
        // One pong per ping written (#892): each ping its send thread writes grants one credit, and a pong takes one as
        // it arrives, whatever it echoes. A pong with none is dropped here, never queued: a client cannot send guesses
        // of a stamp, or a flood for the tick to dispatch. After the admission check, so no credit is spent or probed
        // before it. At Trace: a client can send them at any rate.
        if (header.Type == NetworkPacketType.CMSG_PONG && _sender?.TryTakePongCredit() != true)
        {
            ReceiveLog.PongWithoutPing(_logger, header.Type);
            return ValueTask.CompletedTask;
        }
        if (_worldSessionFilter.CanProcess(header.Type) || _worldMapFilter.CanProcess(header.Type))
        {
            // The depth cap (#927): a packet that arrives with Network:MaxReceiveQueueDepth already waiting is not
            // queued and closes the connection, so the depth never passes the cap. The tick's count is read only when
            // the read loop's own figure, worked out from that count as it last read it, reaches the cap: the figure
            // is never below the real depth, so under the cap there is nothing to ask, and a connection the tick
            // keeps up with reads the tick's field once in about a cap's worth of packets.
            uint queued = _receiveLoop.Queued;
            if (queued - _receiveLoop.HandledSeen >= _receiveLoop.MaxDepth)
            {
                _receiveLoop.HandledSeen = Volatile.Read(ref _handledPackets);
                if (queued - _receiveLoop.HandledSeen >= _receiveLoop.MaxDepth)
                {
                    CloseOverReceiveQueueDepth(header.Type);
                    return ValueTask.CompletedTask;
                }
            }

            // Counted before the tick can take it, so the tick's count never passes this one.
            Volatile.Write(ref _receiveLoop.Queued, queued + 1);
            _receiveQueue.Enqueue(new WorldPacket(header.Type, payload, _time.GetUtcNow().UtcTicks));
            return ValueTask.CompletedTask;
        }
        DropUnaccepted(header.Type);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// The connection has its cap of packets waiting and sent another (#927): closed from the read loop, without a
    /// notice, as a connection that drops is. Once per connection: <see cref="Connection.Close" /> marks it closing
    /// before it returns, and the read loop, the only caller, delivers nothing more to a closing connection: it
    /// discards what it reads from then on undecoded, and <see cref="OnReceive" />'s first check stands behind that.
    /// What is queued is
    /// dropped by the tick's next pass, which finds the connection no longer authorized; the session ends and the
    /// character is saved on the tick, as on any disconnect (<see cref="OnClose" />).
    /// </summary>
    private void CloseOverReceiveQueueDepth(NetworkPacketType type)
    {
        ReceiveLog.ReceiveQueueOverDepth(_logger, Id, AccountId?.Value, type, _receiveLoop.MaxDepth);
        _receiveQueueLimit.Kicked();
#pragma warning disable MA0045 // the read loop must not wait on the close it starts
        Close(false);
#pragma warning restore MA0045
    }

    // Read and written only by this connection's receive loop, which delivers one packet at a time.
    private bool _warnedUnhandled;

    /// <summary>
    /// An admitted connection's packet that no filter accepts at arrival is dropped here, never handed to
    /// <see cref="IServerBase.CallListener" />, which runs a handler on the receive path, off the tick (#861).
    /// A packet a filter takes in another state (input sent between the select and the spawn, which the client
    /// does on every login, or a select-phase packet once in the world) is expected, and logged at Debug (input
    /// at Trace, as it arrives many times a second). One
    /// no filter takes in any state (an opcode missing its filter entry, or a client sending what it should not)
    /// is logged at Warning, once per connection, then at Debug.
    /// </summary>
    private void DropUnaccepted(NetworkPacketType type)
    {
        if (WorldSessionFilter.IsSessionPacket(type) || MapSessionFilter.IsMapPacket(type))
        {
            // Input (60 Hz before every spawn) is kept out of Debug, as the receive loop's "IN:" line does.
            if (type == NetworkPacketType.CMSG_PLAYER_INPUT)
                ReceiveLog.DroppedForStateFrequent(_logger, type);
            else
                ReceiveLog.DroppedForState(_logger, type);
        }
        else if (!_warnedUnhandled)
        {
            _warnedUnhandled = true;
            ReceiveLog.NoHandler(_logger, type);
        }
        else
        {
            ReceiveLog.NoHandlerRepeated(_logger, type);
        }
    }

    /// <summary>
    /// A packet the world runs in some state, so one a client sealing as told seals (#875): the version handshake and every
    /// filtered packet but the pong, which is plain by design. Admission is handled before this is asked. An opcode no world
    /// filter takes (CMSG_PING, the auth server's) is dropped as before, whatever its flags, as the
    /// client contract (Avalon.Client#213) promises for the opcodes declared ClearText.
    /// </summary>
    private static bool MustArriveSealed(NetworkPacketType type) =>
        type != NetworkPacketType.CMSG_PONG
        && (type == NetworkPacketType.CMSG_WORLD_HANDSHAKE || WorldSessionFilter.IsSessionPacket(type)
            || MapSessionFilter.IsMapPacket(type));

    private static partial class ReceiveLog
    {
        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Closed connection {ConnectionId}: {PacketType} arrived unsealed while this world seals packets (Network:PacketEncryption)")]
        public static partial void PlainPacketRefused(ILogger logger, Guid connectionId, NetworkPacketType packetType);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Closed connection {ConnectionId} of account {AccountId}: {PacketType} arrived with {MaxDepth} packets already waiting for the tick (Network:MaxReceiveQueueDepth)")]
        public static partial void ReceiveQueueOverDepth(ILogger logger, Guid connectionId, long? accountId,
            NetworkPacketType packetType, uint maxDepth);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Dropped {PacketType}: not accepted in the connection's current state")]
        public static partial void DroppedForState(ILogger logger, NetworkPacketType packetType);

        [LoggerMessage(Level = LogLevel.Trace, Message = "Dropped {PacketType}: not accepted in the connection's current state")]
        public static partial void DroppedForStateFrequent(ILogger logger, NetworkPacketType packetType);

        // A stale in-map packet, dropped by the session pass on the tick. A client causes one with every packet it
        // sends between its leave and the answer to it, so the lines check the level before anything is built.
        [LoggerMessage(Level = LogLevel.Debug, Message = "Dropped {PacketType} queued for a character that has left the world")]
        public static partial void DroppedForCharacterGone(ILogger logger, NetworkPacketType packetType);

        [LoggerMessage(Level = LogLevel.Trace, Message = "Dropped {PacketType} queued for a character that has left the world")]
        public static partial void DroppedForCharacterGoneFrequent(ILogger logger, NetworkPacketType packetType);

        [LoggerMessage(Level = LogLevel.Trace, Message = "Dropped {PacketType}: every ping written has had its one answer")]
        public static partial void PongWithoutPing(ILogger logger, NetworkPacketType packetType);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Could not find a handler for packet {PacketType}; further ones on this connection are logged at Debug")]
        public static partial void NoHandler(ILogger logger, NetworkPacketType packetType);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Could not find a handler for packet {PacketType}")]
        public static partial void NoHandlerRepeated(ILogger logger, NetworkPacketType packetType);
    }

    protected override void OnPacketAccounted(NetworkPacketType type, int size)
    {
        DiagnosticsConfig.World.BytesReceived.Add(size);
        DiagnosticsConfig.World.PacketsReceived.Add(1);
    }

    protected override void OnFrameDiscarded(int size) => DiagnosticsConfig.World.BytesReceived.Add(size);

    protected override void OnFrameRefused() => DiagnosticsConfig.World.FrameKicks.Add(1);

    protected override void OnHandshakeFailed(string reason) =>
        DiagnosticsConfig.World.HandshakeFailures.Add(1, new KeyValuePair<string, object?>("reason", reason));

    protected override void OnProtocolViolation(string reason) =>
        DiagnosticsConfig.World.ProtocolKicks.Add(1, new KeyValuePair<string, object?>("reason", reason));

    protected override long? AccountIdForLog => AccountId?.Value;

    protected override long GetServerTime() => Server.ServerTime;

    private readonly record struct WorldPacket(NetworkPacketType Type, Packet? Payload, long ArrivedTicks);

    #region Filters

    private readonly WorldSessionFilter _worldSessionFilter;
    private readonly MapSessionFilter _worldMapFilter;
    private readonly Func<WorldPacket, bool> _sessionFilterPredicate;
    private readonly Func<WorldPacket, bool> _mapFilterPredicate;

    #endregion

    #region Callback Processing

    private interface IContinuation
    {
        Task Work { get; }
        bool IsReady { get; }
        bool IsSuccess { get; }
        Exception? Error { get; }
        void Execute();
    }

    private sealed class Continuation : IContinuation
    {
        private readonly Task _task;
        private readonly Action _callback;

        internal Continuation(Task task, Action callback)
        {
            _task = task;
            _callback = callback;
        }

        public Task Work => _task;
        public bool IsReady => _task.IsCompleted;
        public bool IsSuccess => _task.IsCompletedSuccessfully;
        public Exception? Error => _task.Exception;
        public void Execute() => _callback();
    }

    private sealed class Continuation<T> : IContinuation
    {
        private readonly Task<T> _task;
        private readonly Action<T> _callback;

        internal Continuation(Task<T> task, Action<T> callback)
        {
            _task = task;
            _callback = callback;
        }

        public Task Work => _task;
        public bool IsReady => _task.IsCompleted;
        public bool IsSuccess => _task.IsCompletedSuccessfully;
        public Exception? Error => _task.Exception;
        public void Execute() => _callback(_task.Result); // only called after IsSuccess is verified
    }

    public void EnqueueContinuation<T>(Task<T> task, Action<T> callback)
        => _continuationQueue.Enqueue(new Continuation<T>(task, callback));

    public void EnqueueContinuation(Task task, Action callback)
        => _continuationQueue.Enqueue(new Continuation(task, callback));

    /// <inheritdoc />
    public void EnqueueSettled<T>(Task<T> task, Action<Task<T>> callback)
        => _continuationQueue.Enqueue(new SettledContinuation<T>(task, callback));

    /// <summary>
    /// A continuation the drain runs whatever its task's outcome (<see cref="ISettledContinuations" />): to the drain it
    /// is one that has succeeded as soon as its task has ended, so it takes the same path as the others, in the same
    /// order, with a throw contained the same way. The callback is handed the task and reads the outcome.
    /// </summary>
    private sealed class SettledContinuation<T> : IContinuation
    {
        private readonly Task<T> _task;
        private readonly Action<Task<T>> _callback;

        internal SettledContinuation(Task<T> task, Action<Task<T>> callback)
        {
            _task = task;
            _callback = callback;
        }

        public Task Work => _task;
        public bool IsReady => _task.IsCompleted;
        public bool IsSuccess => true; // ended is enough: the callback reads the outcome
        public Exception? Error => null;
        public void Execute() => _callback(_task);
    }

    private void ProcessContinuations()
    {
        if (_continuationQueue.IsEmpty)
            return;

        // Snapshot the count before draining so re-enqueued items land past
        // this boundary and are deferred to the next tick rather than
        // spinning in the same invocation.
        int count = _continuationQueue.Count;
        int processed = 0;

        while (processed++ < count && _continuationQueue.TryDequeue(out IContinuation? item))
        {
            // Readiness first, and only once it holds is the outcome read (#704). The task finishes on
            // the thread pool while this runs on the tick, so the other order, success then readiness,
            // could see it unfinished and then finished: a task that succeeded between the two reads was
            // taken for a faulted one and its callback dropped for good. A finished task never changes
            // outcome, so success read after readiness is final.
            if (!item.IsReady)
            {
                _continuationQueue.Enqueue(item); // counts against budget — deferred to next tick
                continue;
            }

            if (item.IsSuccess)
            {
                // Contained per callback, as ProcessQueue contains each packet handler: one that
                // throws is one request failing. Escaping, it would end this tick's flush for every
                // connection after this one and hold back the rest of this connection's queue.
                try
                {
                    item.Execute();
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Continuation callback threw");
                }
            }
            else
            {
                _logger.LogError(item.Error, "Continuation faulted");
            }
        }
    }

    #endregion
}
