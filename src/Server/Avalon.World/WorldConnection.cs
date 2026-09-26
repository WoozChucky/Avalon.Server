using System.Collections.Concurrent;
using System.Net.Sockets;
using Avalon.Common;
using Avalon.Common.Telemetry;
using Avalon.Common.ValueObjects;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;
using Avalon.Common.Accounts;
using Avalon.World.Entities;
using Avalon.World.Filters;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Logging;
using Packet = Avalon.Network.Packets.Packet;

namespace Avalon.World;

public class WorldConnection : Connection, IWorldConnection, IAccessLevelAssignable
{
    private readonly ConcurrentQueue<IContinuation> _continuationQueue = new();

    private readonly ConcurrentQueue<WorldPacket> _receiveQueue;

    private readonly IWorldServer _server;

    private CharacterEntity? _characterEntity;

    private long _lastClientTicks;
    private long _lastServerTicks;

    public WorldConnection(IWorldServer server, TcpClient client, ILoggerFactory loggerFactory,
        IPacketReader packetReader)
        : base(loggerFactory.CreateLogger<WorldConnection>(), (server as IServerBase)!, packetReader)
    {
        _server = server;
        _receiveQueue = new ConcurrentQueue<WorldPacket>();
        _worldSessionFilter = new WorldSessionFilter(this);
        _worldMapFilter = new MapSessionFilter(this);
        _sessionFilterPredicate = wp => _worldSessionFilter.CanProcess(wp.Type);
        _mapFilterPredicate = wp => _worldMapFilter.CanProcess(wp.Type);
        Init(client);
    }

    public AccountId? AccountId { get; set; }

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

    public void BeginSelect(long nowTicks) => _selectStartedTicks = nowTicks;

    public void CancelSelect() => _selectStartedTicks = 0;

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
        return pending;
    }

    public long Latency { get; private set; }
    public long RoundTripTime { get; private set; }

    /// <summary>Server time minus client time, in ticks, as of the last pong.</summary>
    public long TimeSyncOffset { get; private set; }

    /// <inheritdoc />
    public long CurrentPacketArrivedTicks { get; private set; }
    public bool InGame => Character != null;
    public bool InMap => InGame && _characterEntity?.Map > 0;

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

    public void SendTimeSyncPing()
    {
        _lastServerTicks = DateTime.UtcNow.Ticks;
        Send(SPingPacket.Create(_lastServerTicks, _lastClientTicks, RoundTripTime, TimeSyncOffset));
    }

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
        // (it is assigned below), and _lastServerTicks can already have advanced if a ping went out
        // before this pong landed -- either one turns the offset into the gap between pings.
        TimeSyncOffset = lastServerTimestamp + rtt / 2 - clientReceivedTimestamp;

        if (Latency - latency > 20)
        {
            _logger.LogInformation("[{CharName}] Latency changed: {Latency}ms -> {NewLatency}ms",
                Character?.Name ?? AccountId?.ToString(), Latency, latency);
        }

        _logger.LogTrace("[{CharName}] RTT: {Rtt}ticks, Latency: {Latency}ms", Character?.Name ?? AccountId?.ToString(),
            rtt, latency);

        _lastClientTicks = clientReceivedTimestamp;
        RoundTripTime = rtt;
        Latency = latency;
    }


    public void UpdateSession()
    {
        ProcessQueue(_sessionFilterPredicate);
    }

    public void UpdateMap()
    {
        ProcessQueue(_mapFilterPredicate);
    }

    public void FlushContinuations()
    {
        ProcessContinuations();
    }

    private void ProcessQueue(Func<WorldPacket, bool> predicate)
    {
        const uint MaxPacketsPerUpdate = 150;
        uint processedPackets = 0;

        // Peek-first: if the front packet doesn't pass the filter, leave it for the other pass.
        // TryDequeue after TryPeek is safe — only the tick thread dequeues (SPSC).
        while (IsConnected &&
               _receiveQueue.TryPeek(out WorldPacket packet) &&
               predicate(packet))
        {
            _receiveQueue.TryDequeue(out _);
            try
            {
                CurrentPacketArrivedTicks = packet.ArrivedTicks;
                if (_server.PacketHandlers.TryGetValue(packet.Type, out IWorldPacketHandler? handler))
                    handler.Execute(this, packet.Payload!);
                else
                    _logger.LogWarning("No handler for packet {PacketType}", packet.Type);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing packet {PacketType}", packet.Type);
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

    public void AssignAccessLevel(AccountAccessLevel level) => AccessLevel = level;

    public override void Send(NetworkPacket packet)
    {
        DiagnosticsConfig.World.BytesSent.Add(packet.Size);
        DiagnosticsConfig.World.PacketsSent.Add(1);
        base.Send(packet);
    }

    protected override IOutbox OnCreateOutbox() =>
        new TickDrivenOutbox(Id, _logger, Server.SendBufferCapacity,
#pragma warning disable MA0045 // the fault callback is synchronous, and it fires from inside the outbox this close then disposes
            onFault: () => Close(false));
#pragma warning restore MA0045

    public void FlushOutbox() => _outbox?.Flush();

    public void InitOutboxForTest(PacketStream stream)
    {
        _outbox = OnCreateOutbox();
        _outbox.Connect(stream);
    }

    protected override void OnHandshakeFinished() => Server.CallConnectionListener(this);

    protected override Task<PacketStream> GetStream(TcpClient client) =>
        Task.FromResult(new PacketStream(new NetworkStream(client.Client, true)));

    protected override async Task OnClose(bool expected = true)
    {
        // DeSpawnPlayer mutates MapInstance dictionaries — defer to the tick thread.
        (Server as WorldServer)!.EnqueueDisconnect(this);
        await (Server as WorldServer)!.ClearInWorldFlagAsync(AccountId);
        await Server.RemoveConnection(this);
    }

    protected override ValueTask OnReceive(NetworkPacketHeader header, Packet? payload)
    {
        if (_worldSessionFilter.CanProcess(header.Type) || _worldMapFilter.CanProcess(header.Type))
        {
            _receiveQueue.Enqueue(new WorldPacket(header.Type, payload, DateTime.UtcNow.Ticks));
            return ValueTask.CompletedTask;
        }
        return new ValueTask(Server.CallListener(this, header, payload));
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
            else if (!item.IsReady)
                _continuationQueue.Enqueue(item); // counts against budget — deferred to next tick
            else
                _logger.LogError(item.Error, "Continuation faulted");
        }
    }

    #endregion
}
