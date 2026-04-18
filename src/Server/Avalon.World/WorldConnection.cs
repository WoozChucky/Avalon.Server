using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net.Sockets;
using Avalon.Common.Telemetry;
using Avalon.Common.Threading;
using Avalon.Common.ValueObjects;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;
using Avalon.World.Entities;
using Avalon.World.Filters;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Microsoft.Extensions.Logging;
using Packet = Avalon.Network.Packets.Packet;

namespace Avalon.World;

public class WorldConnection : Connection, IWorldConnection
{
    private readonly ConcurrentQueue<IContinuation> _continuationQueue = new();

    private readonly LockedQueue<WorldPacket> _receiveQueue;

    private readonly IWorldServer _server;
    private ObservableGauge<double> _bytesReceivedRate;
    private ObservableGauge<double> _bytesSentRate;

    private CharacterEntity? _characterEntity;

    private long _lastClientTicks;
    private long _lastServerTicks;
    private ObservableGauge<double> _packetReceivedRate;
    private ObservableGauge<double> _packetSentRate;
    private long _timeSyncOffset;

    public WorldConnection(IWorldServer server, TcpClient client, ILoggerFactory loggerFactory,
        IPacketReader packetReader)
        : base(loggerFactory.CreateLogger<WorldConnection>(), (server as IServerBase)!, packetReader)
    {
        _server = server;
        _receiveQueue = new LockedQueue<WorldPacket>();
        _worldSessionFilter = new WorldSessionFilter(this);
        _worldMapFilter = new MapSessionFilter(this);
        _sessionFilterPredicate = wp => _worldSessionFilter.CanProcess(wp.Type);
        _mapFilterPredicate = wp => _worldMapFilter.CanProcess(wp.Type);
        Init(client);

        _packetSentRate = DiagnosticsConfig.World.Meter.CreateObservableGauge("network.out.packets.rate",
            () => PacketSentRate, "packets/s", "Rate of packets sent");
        _packetReceivedRate = DiagnosticsConfig.World.Meter.CreateObservableGauge("network.in.packets.rate",
            () => PacketReceivedRate, "packets/s", "Rate of packets received");
        _bytesSentRate = DiagnosticsConfig.World.Meter.CreateObservableGauge("network.out.bytes.rate",
            () => BytesSentRate, "bytes/s", "Rate of bytes sent");
        _bytesReceivedRate = DiagnosticsConfig.World.Meter.CreateObservableGauge("network.in.bytes.rate",
            () => BytesReceivedRate, "bytes/s", "Rate of bytes received");
    }

    public AccountId? AccountId { get; set; }

    public ICharacter? Character
    {
        get => _characterEntity;
        set => _characterEntity = value as CharacterEntity;
    }

    public long Latency { get; private set; }
    public long RoundTripTime { get; private set; }
    public bool InGame => Character != null;
    public bool InMap => InGame && _characterEntity?.Map > 0;

    public void EnableTimeSyncWorker() => _ = Task.Factory.StartNew(TimeSyncWorker, CancellationTokenSource!.Token,
        TaskCreationOptions.LongRunning, TaskScheduler.Default);

    public void OnPongReceived(long lastServerTimestamp, long clientReceivedTimestamp, long clientSentTimestamp)
    {
        long rtt = clientReceivedTimestamp - lastServerTimestamp + (DateTime.UtcNow.Ticks - clientSentTimestamp);
        long latency = rtt / TimeSpan.TicksPerMillisecond;

        _timeSyncOffset = _lastServerTicks + rtt / 2 - _lastClientTicks;

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

        while (IsConnected &&
               _receiveQueue.Next(out WorldPacket packet, predicate))
        {
            try
            {
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

    public double LastMovementTime { get; set; }

    public override void Send(NetworkPacket packet)
    {
        DiagnosticsConfig.World.BytesSent.Add(packet.Size);
        DiagnosticsConfig.World.PacketsSent.Add(1);
        base.Send(packet);
    }

    private async Task TimeSyncWorker()
    {
        bool firstIteration = true;
        try
        {
            while (!CancellationTokenSource!.Token.IsCancellationRequested)
            {
                _lastServerTicks = DateTime.UtcNow.Ticks;

                Send(SPingPacket.Create(_lastServerTicks, _lastClientTicks, RoundTripTime, _timeSyncOffset));

                await Task.Delay(TimeSpan.FromSeconds(firstIteration ? 2 : 10), CancellationTokenSource!.Token);
                firstIteration = false;
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore
        }
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

    protected override async Task OnReceive(NetworkPacketHeader header, Packet? payload)
    {
        if (_worldSessionFilter.CanProcess(header.Type) || _worldMapFilter.CanProcess(header.Type))
        {
            _receiveQueue.Add(new WorldPacket(header.Type, payload));
        }
        else
        {
            await Server.CallListener(this, header, payload);
        }
    }

    protected override void OnPacketAccounted(int size)
    {
        DiagnosticsConfig.World.BytesReceived.Add(size);
        DiagnosticsConfig.World.PacketsReceived.Add(1);
    }

    protected override long GetServerTime() => Server.ServerTime;

    private readonly record struct WorldPacket(NetworkPacketType Type, Packet? Payload);

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
                item.Execute();
            else if (!item.IsReady)
                _continuationQueue.Enqueue(item); // counts against budget — deferred to next tick
            else
                _logger.LogError(item.Error, "Continuation faulted");
        }
    }

    #endregion
}
