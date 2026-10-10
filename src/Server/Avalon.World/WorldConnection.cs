using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using Avalon.Common;
using Avalon.Common.Accounts;
using Avalon.Common.GameAuth;
using Avalon.Common.Telemetry;
using Avalon.Common.ValueObjects;
using Avalon.Hosting.Networking;
using Avalon.Hosting.Telemetry;
using Avalon.Network.Packets.Abstractions;
using Avalon.World.Entities;
using Avalon.World.Filters;
using Avalon.World.Handlers;
using Avalon.World.GameAuth;
using Avalon.World.Maintenance;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Logging;
using Packet = Avalon.Network.Packets.Packet;

namespace Avalon.World;

public partial class WorldConnection : Connection, IWorldConnection, IAccessLevelAssignable, ICharacterLeaveControl,
    IMaintenanceBlockable
{
    private readonly ConcurrentQueue<IContinuation> _continuationQueue = new();

    private readonly ConcurrentQueue<WorldPacket> _receiveQueue;

    /// <summary>Packets received and not yet dispatched; read by the receive-queue gauge off the tick.</summary>
    internal int ReceiveQueueDepth => _receiveQueue.Count;

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
        : base(loggerFactory.CreateLogger<WorldConnection>(), (server as IServerBase)!, packetReader)
    {
        _server = server;
        _packetEncryption = server.PacketEncryption;
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

    public long Latency { get; private set; }
    public long RoundTripTime { get; private set; }

    /// <summary>Server time minus client time, in ticks, as of the last pong.</summary>
    public long TimeSyncOffset { get; private set; }

    /// <inheritdoc />
    public long CurrentPacketArrivedTicks { get; private set; }
    public bool InGame => Character != null;
    // InGame means Character, which is _characterEntity, is set, so the ?. never yields null here.
#pragma warning disable CS8604
    public bool InMap => InGame && _characterEntity?.Map > 0;
#pragma warning restore CS8604

    // Set by a packet handler and taken by the tick. Both run on the tick thread -- handlers are
    // dispatched from ProcessQueue -- so this needs no interlocking, and would need it the day they
    // do not.
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

    public void OnPongReceived(long lastServerTimestamp, long clientReceivedTimestamp, long clientSentTimestamp,
        long serverReceivedTicks)
    {
        // serverReceivedTicks rather than the clock: this runs on the world tick, so a pong that landed
        // while the server slept out the rest of a tick would otherwise be charged that whole wait --
        // a round trip of one tick on a loopback connection, and half of it again as a clock offset,
        // because splitting an asymmetric round trip down the middle misattributes the difference.
        long rtt = clientReceivedTimestamp - lastServerTimestamp + (serverReceivedTicks - clientSentTimestamp);
        long latency = rtt / TimeSpan.TicksPerMillisecond;

        // Every term from THIS exchange. _lastClientTicks still holds the previous pong's stamp here
        // (it is assigned below), and the sender's last ping stamp can already have advanced if a ping went out
        // before this pong landed -- either one turns the offset into the gap between pings.
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
            while (_receiveQueue.TryDequeue(out _)) { }
            return;
        }

        const uint MaxPacketsPerUpdate = 150;
        uint processedPackets = 0;

        // A substitute server in tests has none.
        PacketDispatchTelemetry telemetry = _server.PacketTelemetry ?? PacketDispatchTelemetry.Disabled;

        // Peek-first: if the front packet doesn't pass the filter, leave it for the other pass.
        // TryDequeue after TryPeek is safe — only the tick thread dequeues (SPSC).
        while (IsConnected && _receiveQueue.TryPeek(out WorldPacket packet))
        {
            if (!predicate(packet))
            {
                if (!dropStaleMapPackets || Character != null || !MapSessionFilter.IsMapPacket(packet.Type))
                    break;

                _receiveQueue.TryDequeue(out _);
                _logger.LogDebug("Dropped {PacketType} queued for a character that has left the world", packet.Type);
                if (++processedPackets > MaxPacketsPerUpdate)
                    break;
                continue;
            }

            _receiveQueue.TryDequeue(out _);
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

            if (++processedPackets > MaxPacketsPerUpdate)
                break;
        }
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
        if (header.Type == NetworkPacketType.CMSG_WORLD_HANDSHAKE && GameSessionLease?.IsActive == true)
            return new ValueTask(Server.CallListener(this, header, payload));
        if (!IsGameplayAuthorized) return ValueTask.CompletedTask;
        if (_worldSessionFilter.CanProcess(header.Type) || _worldMapFilter.CanProcess(header.Type))
        {
            _receiveQueue.Enqueue(new WorldPacket(header.Type, payload, _time.GetUtcNow().UtcTicks));
            return ValueTask.CompletedTask;
        }
        DropUnaccepted(header.Type);
        return ValueTask.CompletedTask;
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

    private static partial class ReceiveLog
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Dropped {PacketType}: not accepted in the connection's current state")]
        public static partial void DroppedForState(ILogger logger, NetworkPacketType packetType);

        [LoggerMessage(Level = LogLevel.Trace, Message = "Dropped {PacketType}: not accepted in the connection's current state")]
        public static partial void DroppedForStateFrequent(ILogger logger, NetworkPacketType packetType);

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
