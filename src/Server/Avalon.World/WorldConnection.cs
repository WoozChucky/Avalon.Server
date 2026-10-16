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
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using Avalon.World.Telemetry;
using Avalon.World.Threading;
using Microsoft.Extensions.Logging;
using Packet = Avalon.Network.Packets.Packet;

namespace Avalon.World;

public partial class WorldConnection : Connection, IWorldConnection, IAccessLevelAssignable, ICharacterLeaveControl,
    ISettledContinuations,
    IMaintenanceBlockable, IDisconnectReasonNote
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

    private CharacterEntity? _characterEntity;

    // The world's clock (#820): packet arrival stamps, and the admission and heartbeat timings. The time-sync ping is
    // stamped on its send thread, by the send scheduler's clock, as it is written (#875).
    private readonly TimeProvider _time;
    private volatile bool _maintenanceBlocked;

    // The key exchange runs only on a world that seals (#878): a plain world's connection builds no key pair and no
    // session, sends no public key, and opens nothing, so a frame flagged Encrypted closes it as sealed_before_key.
    public WorldConnection(IWorldServer server, TcpClient client, ILoggerFactory loggerFactory,
        IPacketReader packetReader, TimeProvider? time = null)
        : base(loggerFactory.CreateLogger<WorldConnection>(), (server as IServerBase)!, packetReader,
            server.PacketEncryption ? new ConnectionKeyExchange() : null)
    {
        _server = server;
        // A substitute server in tests has none.
        _receiveQueueLimit = server.ReceiveQueueLimit ?? ReceiveQueueLimit.Default;
        _receiveLoop.MaxDepth = (uint)_receiveQueueLimit.MaxDepth;
        _time = time ?? TimeProvider.System;
        _nextPingTick = TimeSyncSlot;
        _receiveQueue = new ConcurrentQueue<WorldPacket>();
        _worldSessionFilter = new WorldSessionFilter(this);
        _worldMapFilter = new MapSessionFilter(this);
        // The tick's own authorization, at the tick's time (#977); the filters' CanProcess, which the read loop asks,
        // reads the clock.
        _sessionFilterPredicate = wp => IsGameplayAuthorizedOnTick && _worldSessionFilter.Accepts(wp.Type);
        _mapFilterPredicate = wp => IsGameplayAuthorizedOnTick && _worldMapFilter.Accepts(wp.Type);
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

    /// <summary>
    /// The last network round trip in ticks: the server's own measurement, less the time the client says it held the
    /// ping when that is believable (<see cref="OnPongReceived" />); never negative.
    /// </summary>
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

    /// <summary>
    /// The pings of the start burst after the handshake's own (#1034), so a client has a measured clock offset in about
    /// two seconds rather than a period: the handshake's ping, then these, <see cref="StartBurstSpacingTicks" /> apart.
    /// </summary>
    public const int StartBurstFollowUps = 2;

    /// <summary>The ticks between the pings of the start burst: about a second at 60 Hz.</summary>
    public const int StartBurstSpacingTicks = 60;

    /// <summary>
    /// How long an admitted connection may go without a pong that answers a ping it was sent (#1034) before the tick
    /// closes it, through the drain a disconnect takes. Counted from admission, so a client that never answers is closed
    /// too. A client answers every ping, in every state, so only a dead, stuck or hostile one reaches it: twelve
    /// periodic pings in a row unanswered.
    /// </summary>
    public static readonly TimeSpan LivenessTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// <see cref="NextTimeSyncTick" /> once the handshake has asked for the start burst: due on whatever tick comes next.
    /// </summary>
    public const long StartBurstDue = long.MinValue;

    // The world's ping tick on or after which the time-sync sweep next turns to this connection (#1034): the one field
    // the sweep reads of it each tick. It starts at the connection's slot, so a world past its first period turns to a
    // new connection on its first tick, and then follows the slot (turns that only check liveness until the
    // handshake), StartBurstDue once the handshake asks for the start burst, the burst's spacing while it lasts, and
    // long.MaxValue once the connection is closing. Written by the tick, and once by the read loop
    // (RequestInitialTimeSyncPing, an exchange); the tick writes it back only by a compare-exchange from the value it
    // acted on, so a request that lands while the tick is acting is never lost. One that lands on a closing connection
    // is turned to once more, and put out (OnTimeSyncDue).
    private long _nextPingTick;

    // Spreads the connections' periodic pings over the period, one slot after another as connections are made, whatever
    // their place in the list, which shifts with every removal ahead of them. Process-wide: one world runs per process.
    private static int s_timeSyncSlots;

    // The start burst's follow-up pings still to come, and whether the burst has begun, the handshake having asked for
    // it: until then a turn sends no ping (#1034). Tick only.
    private int _burstPingsLeft;
    private bool _timeSyncStarted;

    // The connection's monotonic clock as of its last pong that answered a ping, or as of its admission before the
    // first (#1034): what the liveness rule measures from. Meaningless until the connection is admitted. Tick only: the
    // admission is published by a tick continuation, a pong is handled by the session pass, and the check is the
    // tick's time-sync sweep.
    private long _lastAnswerTimestamp;

    /// <summary>
    /// The tick of the period, from 0 to <see cref="WorldServer.TimeSyncTicksPeriod" /> less one, on which this
    /// connection's periodic pings go: fixed when it is made.
    /// </summary>
    public int TimeSyncSlot { get; } =
        (int)((uint)Interlocked.Increment(ref s_timeSyncSlots) % WorldServer.TimeSyncTicksPeriod);

    /// <summary>
    /// The world's ping tick on or after which the time-sync sweep next turns to this connection
    /// (<see cref="OnTimeSyncDue" />); <see cref="StartBurstDue" /> once the start burst was asked for. The sweep reads
    /// it every tick, and nothing else of the connection unless it is due.
    /// </summary>
    public long NextTimeSyncTick => Volatile.Read(ref _nextPingTick);

    /// <summary>
    /// Asks for the start burst of time-sync pings (#1034): one on the next tick, whatever this connection's slot, and
    /// two more about a second apart, so the client has a measured clock offset in about two seconds. The version
    /// handshake's handler asks, once per connection, on the read loop.
    /// </summary>
    public void RequestInitialTimeSyncPing() => Interlocked.Exchange(ref _nextPingTick, StartBurstDue);

    /// <summary>
    /// The time-sync sweep's turn for this connection, on a <paramref name="tick" /> at or after
    /// <see cref="NextTimeSyncTick" /> (#1034). A closing connection is turned to no more. Then the liveness rule
    /// (<see cref="CloseIfSilent" />): a connection it closes is not pinged again. Before the handshake asked for the
    /// start burst, that is all a turn does, and the next is on the connection's slot: a pong is not let in before the
    /// handshake, so the first ping is the burst's. After it, one ping, and the next turn: the burst's spacing while
    /// the burst lasts, then the slot, at least a burst spacing on. Tick only; no clock read and no allocation.
    /// </summary>
    public void OnTimeSyncDue(long tick, in TickTime now)
    {
        long acted = Volatile.Read(ref _nextPingTick);

        // A closing connection, closed here or for any other reason, is turned to no more, nor pinged: a handshake
        // that lands after the close and asks for the burst is put out here on the next tick.
        if (IsClosing || CloseIfSilent(now))
        {
            Volatile.Write(ref _nextPingTick, long.MaxValue);
            return;
        }

        long next;
        if (acted == StartBurstDue)
        {
            _timeSyncStarted = true;
            _burstPingsLeft = StartBurstFollowUps;
            next = tick + StartBurstSpacingTicks;
        }
        else if (!_timeSyncStarted)
        {
            // Before the handshake a turn is the liveness check alone: a pong could not be let in yet, so a ping would
            // only spend a stamp. The first ping is the start burst's.
            Interlocked.CompareExchange(ref _nextPingTick, NextSlotTick(tick + 1), acted);
            return;
        }
        else if (_burstPingsLeft > 0)
        {
            // The last of the burst hands over to the slot no sooner than a spacing on.
            next = --_burstPingsLeft > 0 ? tick + StartBurstSpacingTicks : NextSlotTick(tick + StartBurstSpacingTicks);
        }
        else
        {
            next = NextSlotTick(tick + 1);
        }

        SendTimeSyncPing();

        // Fails only when the handshake asked for the burst since the read above: that request stays, due next tick.
        Interlocked.CompareExchange(ref _nextPingTick, next, acted);
    }

    /// <summary>The first tick at or after <paramref name="from" /> that falls on this connection's slot.</summary>
    private long NextSlotTick(long from)
    {
        long wait = (TimeSyncSlot - from % WorldServer.TimeSyncTicksPeriod) % WorldServer.TimeSyncTicksPeriod;
        return from + (wait < 0 ? wait + WorldServer.TimeSyncTicksPeriod : wait);
    }

    /// <summary>
    /// Queues a time-sync ping; its send thread stamps it as it writes it (#875). The sweep's (<see cref="OnTimeSyncDue" />).
    /// </summary>
    public void SendTimeSyncPing() => _sender?.EnqueuePing(RoundTripTime, TimeSyncOffset, _server.MeanTickMicros);

    /// <summary>
    /// The liveness rule (#1034): an admitted connection that has had no pong answering one of its pings for
    /// <see cref="LivenessTimeout" />, counted from its admission, is closed, as a connection that drops is
    /// (<see cref="OnClose" />): the tick then ends its session, saves its character and releases its lease. Counted
    /// in <c>network.in.silent_kicks</c>, with an Information line at most once per
    /// <see cref="FailureLogThrottle.Interval" /> for the whole world, since a network fault can silence many at once. A
    /// connection not yet admitted is the admission timeout's, and one maintenance has blocked drops every packet, its
    /// pongs too, and is maintenance's to close. True when it closed the connection.
    /// </summary>
    /// <remarks>
    /// Tick only: the time-sync sweep asks it on each of the connection's turns (<see cref="OnTimeSyncDue" />), once a
    /// period after the start burst, at the tick's time, so it costs no clock read and no scan of its own. A connection
    /// is closed within a period of the timeout.
    /// </remarks>
    public bool CloseIfSilent(in TickTime now)
    {
        if (IsClosing || _maintenanceBlocked || GameSessionLease is null)
            return false;

        TimeSpan silent = _time.GetElapsedTime(_lastAnswerTimestamp, now.On(_time).Timestamp);
        if (silent < LivenessTimeout)
            return false;

        DiagnosticsConfig.World.SilentKicks.Add(1);
        if (s_silentCloses.TryTake(_time, out int suppressed))
            ReceiveLog.ClosedSilent(_logger, Id, AccountId?.Value, (long)silent.TotalSeconds, suppressed);
#pragma warning disable MA0045 // the tick must not wait on the close it starts
        Close(false);
#pragma warning restore MA0045
        return true;
    }

    private static readonly FailureLogThrottle s_silentCloses = new();

    /// <summary>
    /// Handles a pong (#892), from <see cref="Handlers.PongHandler" /> on the tick. It counts only when
    /// <paramref name="lastServerTimestamp" /> is the stamp of a ping this connection's send thread wrote and no pong has
    /// answered yet; the stamp is then spent. From the four times of the exchange (#1034): the whole round trip is
    /// <paramref name="serverReceivedTicks" /> minus that stamp, both the server's own; the network round trip is that
    /// less the client's hold of the ping (<paramref name="clientSentTimestamp" /> minus
    /// <paramref name="clientReceivedTimestamp" />), unless the hold is negative or longer than the whole round trip,
    /// when it is ignored; and the clock offset, server minus client, is the stamp plus half the network round trip
    /// minus <paramref name="clientReceivedTimestamp" />. An answer also restarts the liveness minute. Any other pong (an
    /// echo of no ping, a second answer to one ping, one that arrived before its stamp) changes nothing. It is called for
    /// a client's pong at most once per ping written: a pong beyond that is dropped as it arrives, before any handler.
    /// Not on <see cref="IWorldConnection" />: what answers a ping is the world's to decide, never a script's.
    /// </summary>
    /// <param name="lastServerTimestamp">The echo of a ping's server timestamp: which ping this answers, nothing more.</param>
    /// <param name="clientReceivedTimestamp">When the client says it received the ping, on its own clock.</param>
    /// <param name="clientSentTimestamp">When the client says it sent the pong, on the same clock.</param>
    /// <param name="serverReceivedTicks">When the pong was read off the socket, NOT when it was handled.</param>
    public void OnPongReceived(long lastServerTimestamp, long clientReceivedTimestamp, long clientSentTimestamp,
        long serverReceivedTicks)
    {
        // The echo only says which ping is answered (#892): it must be the stamp the send thread put on a ping it wrote
        // that no pong has answered yet, and it is taken here. A pong that echoes anything else (forged, sent twice, or
        // answering a ping the record has dropped) changes nothing, the liveness clock included. At Debug, and at most
        // once per ping written: a pong reaches this handler only with a credit (OnReceive).
        if (_sender is null || !_sender.TryTakePingStamp(lastServerTimestamp))
        {
            HighRatePacketLog.PongAnswersNoPing(_logger, NetworkPacketType.CMSG_PONG, Id);
            return;
        }

        // The four times of the exchange: t1, the stamp the server wrote, which the echo now equals; t2 and t3, when the
        // client says it received the ping and sent the pong, on its own clock; t4, the pong's arrival. t1 and t4 are the
        // server's own, so t4 - t1 is the whole round trip, the client's hold of the ping included.
        // serverReceivedTicks rather than the clock: this runs on the world tick, so a pong that landed
        // while the server slept out the rest of a tick would otherwise be charged that whole wait --
        // a round trip of one tick on a loopback connection, and half of it again as a clock offset,
        // because splitting an asymmetric round trip down the middle misattributes the difference.
        long rtt = serverReceivedTicks - lastServerTimestamp;
        if (rtt < 0)
        {
            // The pong arrived before its ping was stamped. Either its echo was sent ahead of the ping (a guess that hit:
            // it came in on the credit of an earlier, unanswered ping), or the wall clock, which both times come from, was
            // set back in between. Nothing was measured, the stamp is spent, and it does not count as an answer.
            HighRatePacketLog.PongBeforeItsPing(_logger, NetworkPacketType.CMSG_PONG, Id);
            return;
        }

        // The client's hold, t3 - t2, comes out of the round trip (#1034): network round trip = (t4 - t1) - (t3 - t2).
        // Believed only when it is not negative and no longer than the whole round trip, else ignored, as before #1034:
        // a client cannot make the round trip negative, nor move its offset by more than half of it. Compared unsigned:
        // with t3 >= t2 the difference fits a ulong whatever the two are, so no pair of garbage times wraps into a hold.
        long network = rtt;
        if (clientSentTimestamp >= clientReceivedTimestamp)
        {
            ulong hold = unchecked((ulong)(clientSentTimestamp - clientReceivedTimestamp));
            if (hold <= (ulong)rtt)
                network = rtt - (long)hold;
        }

        long latency = network / TimeSpan.TicksPerMillisecond;

        // Server time minus client time, from all four times: ((t1 - t2) + (t4 - t3)) / 2, which is t1 + network / 2 - t2,
        // the server's time half a network round trip after its stamp, less the client's at that moment. A client adds
        // it to its clock to read the server's. Every term from THIS exchange: a later ping can already have gone out
        // before this pong landed, and a term of another exchange turns the offset into the gap between pings. The
        // client's times are its clock, and false ones move only its own idea of server time.
        TimeSyncOffset = lastServerTimestamp + network / 2 - clientReceivedTimestamp;

        if (Latency - latency > 20 && _logger.IsEnabled(LogLevel.Information))
        {
            HighRatePacketLog.LatencyChanged(_logger, Character?.Name ?? AccountId?.ToString(), Latency, latency,
                NetworkPacketType.CMSG_PONG, Id);
        }

        if (_logger.IsEnabled(LogLevel.Trace))
        {
            HighRatePacketLog.RoundTrip(_logger, Character?.Name ?? AccountId?.ToString(), network, latency,
                NetworkPacketType.CMSG_PONG, Id);
        }

        RoundTripTime = network;
        Latency = latency;

        // An answer: the liveness clock starts again (#1034), at the tick's time, which the session pass handling this
        // pong was given by the lease check (no clock read per pong).
        _lastAnswerTimestamp = _leaseCheckedAt.On(_time).Timestamp;
    }

    public void UpdateSession()
    {
        WorldTickTrace.CountSessionPackets(ProcessQueue(_sessionFilterPredicate, dropStaleMapPackets: true));
    }

    public void UpdateMap()
    {
        WorldTickTrace.CountMapPackets(ProcessQueue(_mapFilterPredicate, dropStaleMapPackets: false));
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
    /// <returns>
    ///     The packets the pass took off the queue: dispatched (or without a handler), or dropped as stale. Not a held
    ///     head, nor a queue an unauthorised connection discards.
    /// </returns>
    private uint ProcessQueue(Func<WorldPacket, bool> predicate, bool dropStaleMapPackets)
    {
        if (!IsGameplayAuthorizedOnTick)
        {
            if (_hasPendingReceivePacket != 0)
                ReleaseReceivePacket();
            while (_receiveQueue.TryDequeue(out _))
                ReleaseReceivePacket();
            return 0;
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

        return processedPackets;
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
    /// the session, which exists only while the world seals (Network:PacketEncryption); otherwise every packet goes plain
    /// inside TLS, its Encrypted flag cleared.
    /// </summary>
    protected override IOutbox OnCreateOutbox()
    {
        NetworkSendScheduler scheduler = _server.SendScheduler
            ?? throw new InvalidOperationException("The world server has no send scheduler");
#pragma warning disable MA0045 // a slow connection's close starts on its send thread, which must not wait on it
        _sender = scheduler.CreateSender(Id, _logger, KeyExchange?.Session, close: () => Close(false),
            onPingWritten: CountPing);
#pragma warning restore MA0045
        return _sender;
    }

    /// <summary>
    /// Whether this connection's gameplay packets are sealed both ways (Network:PacketEncryption, #875): the world's,
    /// fixed for the connection's life, and true exactly when it has a key exchange (#878). Its sender seals with the
    /// session or sends plain inside TLS, and its admission reply names the mode. While it seals, a packet the world runs
    /// (the handshake and every session- or map-filter packet but the pong) that arrives plain closes the connection;
    /// while it does not, one that arrives sealed does, in the read loop.
    /// </summary>
    public bool PacketEncryption => KeyExchange is not null;

    /// <summary>The connection's session (<see cref="Connection.KeyExchange" />); null on a world that does not seal.</summary>
    public IAvalonCryptoSession? CryptoSession => KeyExchange?.Session;

    /// <summary>The connection's own key pair (<see cref="Connection.KeyExchange" />); null on a world that does not seal.</summary>
    public ICryptoManager? ServerCrypto => KeyExchange?.ServerCrypto;

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
        if (KeyExchange is not null && (header.Flags & NetworkPacketFlags.Encrypted) == 0 && MustArriveSealed(header.Type))
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
        // The goodbye (#967) is noted as it arrives: the client closes right after it, and the close's despawn runs on the
        // tick before any session packet would, so a goodbye left to its handler would end every quit as a drop. Before
        // the depth cap, so a goodbye the cap closes the connection on still counts. The handler closes a connection the
        // client left open.
        if (header.Type == NetworkPacketType.CMSG_LEAVE_WORLD) NoteGoodbye();
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

    /// <summary>
    /// Admission refused the client's public key on a world that seals: the right length, and not a P-256 point
    /// (<see cref="InvalidPublicKeyException" />). What the peer sent, not a server fault: one Warning, of fixed text and
    /// without the exception, and one count under <c>bad_public_key</c>, as the read loop's refusals are (#897). Once per
    /// connection: admission runs once, and the caller closes the connection.
    /// </summary>
    public void ReportBadPublicKey(long accountId)
    {
        ReceiveLog.BadPublicKey(_logger, Id, accountId, RemoteEndPoint);
        OnProtocolViolation(ProtocolViolation.BadPublicKey);
    }

    /// <summary>Keying the session from a valid client key failed: the server's fault, logged with its exception.</summary>
    public void ReportKeyingFailed(Exception error) => ReceiveLog.KeyingFailed(_logger, error, Id);

    /// <summary>Publishing an admitted connection, or sending its reply, failed: the server's fault, logged with its exception.</summary>
    public void ReportAdmissionFailed(Exception error) => ReceiveLog.AdmissionFailed(_logger, error, Id);

    private static readonly FailureLogThrottle s_overloadedAdmissions = new();
    private static readonly FailureLogThrottle s_failedAdmissions = new();
    private static readonly FailureLogThrottle s_overBudgetAdmissions = new();

    private static readonly KeyValuePair<string, object?> s_inFlightReason = new("reason", "in_flight");
    private static readonly KeyValuePair<string, object?> s_rateReason = new("reason", "rate");

    /// <summary>
    /// This connection's source is over its admission budget (#1052, <see cref="AdmissionSourceBudget" />): it has
    /// <paramref name="limit" /> admissions in flight, or its IPv6 /56 has, or it (or its /56) started that many in the
    /// last minute.
    /// The admission is answered <c>ServiceUnavailable</c> and closed at once, on the receive path that delivered it,
    /// before its ticket is redeemed. Counted under <c>world.admission.source_refusals</c>, <c>reason</c>
    /// <c>in_flight</c> (the source's cap or its /56's) or <c>rate</c>. A Warning naming the source as it is counted (an
    /// IPv4 address, an IPv6 /64, or the /56 a block limit counts), and the limit, without an exception, at most once per
    /// <see cref="FailureLogThrottle.Interval" /> for the whole world, with how many more were refused since: a flood
    /// from one source writes one line every 10 s, not one per try.
    /// </summary>
    public void ReportAdmissionOverSourceBudget(AdmissionSourceRefusal refusal, int limit)
    {
        DiagnosticsConfig.World.AdmissionSourceRefusals.Add(1,
            refusal is AdmissionSourceRefusal.Rate or AdmissionSourceRefusal.IPv6BlockRate ? s_rateReason : s_inFlightReason);
        if (s_overBudgetAdmissions.TryTake(_time, out int suppressed))
        {
            string source = ClientIPAddress is { } address ? AdmissionSourceBudget.Describe(address, refusal) : ClientAddress;
            string budget = refusal switch
            {
                AdmissionSourceRefusal.Rate => "admissions started in the last minute (World:Admission:MaxPerSourcePerMinute)",
                AdmissionSourceRefusal.IPv6BlockRate => "admissions started from its /56 in the last minute (World:Admission:MaxPerIPv6BlockPerMinute)",
                AdmissionSourceRefusal.IPv6BlockInFlight => "admissions in flight from its /56 (World:Admission:MaxInFlightPerIPv6Block)",
                _ => "admissions in flight (World:Admission:MaxInFlightPerSource)",
            };
            ReceiveLog.AdmissionOverSourceBudget(_logger, source, limit, budget, suppressed);
        }
    }

    /// <summary>
    /// The admission work queue refused to redeem this connection's join ticket (#1050), which the admission answers
    /// <c>ServiceUnavailable</c> and closes at once, on the receive path that delivered it, never on the tick. The queue
    /// counted it as it refused it (<c>world.work.refusals</c>, lane <c>admission</c>). Expected under a reconnect storm:
    /// a Warning, without an exception, at most once per <see cref="FailureLogThrottle.Interval" /> for the whole world.
    /// </summary>
    public void ReportAdmissionOverloaded()
    {
        if (s_overloadedAdmissions.TryTake(_time, out int held))
            ReceiveLog.AdmissionOverloaded(_logger, WorldDatabaseWork.AdmissionOutstandingLimit, held);
    }

    /// <summary>
    /// The redemption of this connection's join ticket faulted, or was cancelled, once started (#1050): the admission is
    /// answered <c>ServiceUnavailable</c> and closed by its continuation. Counted under <c>world.admission.failures</c>
    /// (a work queue refusal that arrives this way is logged as one, and was counted by its queue). A defect: an Error with its
    /// exception, at most once per <see cref="FailureLogThrottle.Interval" /> for the whole world, so a storm of them
    /// cannot flood the log. Called on the thread pool, where the redemption ended, never on the tick, so a connection
    /// gone before its continuation ran is counted too.
    /// </summary>
    public void ReportRedemptionFailed(Exception error)
    {
        if (error is WorldWorkUnavailableException)
        {
            ReportAdmissionOverloaded();
            return;
        }

        DiagnosticsConfig.World.AdmissionFailures.Add(1);
        if (s_failedAdmissions.TryTake(_time, out int suppressed))
            ReceiveLog.RedemptionFailed(_logger, error, error.GetType().Name, suppressed);
    }

    private static partial class ReceiveLog
    {
        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Dropped a continuation: a full work queue refused its work; {Suppressed} more were refused since the last line")]
        public static partial void ContinuationRefused(ILogger logger, int suppressed);

        [LoggerMessage(Level = LogLevel.Error, Message = "Continuation faulted; {Suppressed} more faulted since the last line")]
        public static partial void ContinuationFaulted(ILogger logger, Exception? error, int suppressed);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Answered an admission ServiceUnavailable: the world's admission work queue is full ({Outstanding} outstanding); {Suppressed} more were refused since the last line")]
        public static partial void AdmissionOverloaded(ILogger logger, int outstanding, int suppressed);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Answered an admission ServiceUnavailable: its source {ClientSource} already has {Limit} {Budget}; {Suppressed} more were refused since the last line")]
        public static partial void AdmissionOverSourceBudget(ILogger logger, string clientSource, int limit, string budget, int suppressed);

        [LoggerMessage(Level = LogLevel.Error,
            Message = "Answered an admission ServiceUnavailable: its redemption threw {ExceptionType}; {Suppressed} more failed since the last line")]
        public static partial void RedemptionFailed(ILogger logger, Exception error, string exceptionType, int suppressed);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Closing connection {ConnectionId} of account {AccountId} from {RemoteEndPoint}: its admission's public key is not a P-256 point")]
        public static partial void BadPublicKey(ILogger logger, Guid connectionId, long accountId, string remoteEndPoint);

        [LoggerMessage(Level = LogLevel.Error,
            Message = "Closing connection {ConnectionId}: its session could not be keyed from a valid client key")]
        public static partial void KeyingFailed(ILogger logger, Exception error, Guid connectionId);

        [LoggerMessage(Level = LogLevel.Error,
            Message = "Closing connection {ConnectionId}: its admission could not be published or answered")]
        public static partial void AdmissionFailed(ILogger logger, Exception error, Guid connectionId);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Closed connection {ConnectionId}: {PacketType} arrived unsealed while this world seals packets (Network:PacketEncryption)")]
        public static partial void PlainPacketRefused(ILogger logger, Guid connectionId, NetworkPacketType packetType);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Closed connection {ConnectionId} of account {AccountId}: {PacketType} arrived with {MaxDepth} packets already waiting for the tick (Network:MaxReceiveQueueDepth)")]
        public static partial void ReceiveQueueOverDepth(ILogger logger, Guid connectionId, long? accountId,
            NetworkPacketType packetType, uint maxDepth);

        [LoggerMessage(Level = LogLevel.Information,
            Message = "Closed connection {ConnectionId} of account {AccountId}: no pong answered a ping for {SilentSeconds} s; {Suppressed} more were closed so since the last line")]
        public static partial void ClosedSilent(ILogger logger, Guid connectionId, long? accountId, long silentSeconds,
            int suppressed);

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

    /// <summary>The world's clock times a started frame too (#938).</summary>
    protected override TimeProvider ReadClock => _time;

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
                ReportFaultedContinuation(item.Error);
            }
        }
    }

    private static readonly FailureLogThrottle s_refusedContinuations = new();
    private static readonly FailureLogThrottle s_faultedContinuations = new();

    /// <summary>
    /// A continuation whose task did not succeed, so its callback does not run (#1050). A work queue's refusal
    /// (<see cref="WorldWorkUnavailableException" />) is expected under load, a reconnect storm's character selects
    /// above all: a Warning without the exception, whose stack is not the caller's. Any other fault is logged with its
    /// exception, at Error, as before. Each at most once per <see cref="FailureLogThrottle.Interval" /> for the whole
    /// world, with how many were held back, where every one was a line with a stack. On the tick, faulted tasks only.
    /// </summary>
    private void ReportFaultedContinuation(Exception? error)
    {
        int suppressed;
        if (((error as AggregateException)?.InnerException ?? error) is WorldWorkUnavailableException)
        {
            if (s_refusedContinuations.TryTake(_time, out suppressed))
                ReceiveLog.ContinuationRefused(_logger, suppressed);
            return;
        }

        if (s_faultedContinuations.TryTake(_time, out suppressed))
            ReceiveLog.ContinuationFaulted(_logger, error, suppressed);
    }

    #endregion
}
