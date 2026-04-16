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
    private readonly ConcurrentQueue<(Task<object>, Action<object>)> _genericTaskQueue = new();

    private readonly LockedQueue<WorldPacket> _receiveQueue;

    private readonly IWorldServer _server;
    private readonly ConcurrentQueue<(Task, Action)> _taskQueue = new();
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


    public void UpdateSession(TimeSpan deltaTime)
    {
        ProcessQueue(_worldSessionFilter);
        ProcessContinuations();
    }

    public void UpdateMap(TimeSpan deltaTime)
    {
        ProcessQueue(_worldMapFilter);
        ProcessContinuations();
    }

    private void ProcessQueue(PacketFilter filter)
    {
        const uint MaxPacketsPerUpdate = 150;
        uint processedPackets = 0;

        while (IsConnected &&
               _receiveQueue.Next(out WorldPacket? packet, worldPacket => filter.CanProcess(worldPacket.Type)))
        {
            if (packet == null)
            {
                _logger.LogWarning("Received null packet");
                break;
            }

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

    protected override async Task OnReceive(NetworkPacket packet, Packet? payload)
    {
        DiagnosticsConfig.World.BytesReceived.Add(packet.Size);
        DiagnosticsConfig.World.PacketsReceived.Add(1);

        if (_worldSessionFilter.CanProcess(packet.Header.Type) || _worldMapFilter.CanProcess(packet.Header.Type))
        {
            _receiveQueue.Add(new WorldPacket {Type = packet.Header.Type, Payload = payload});
        }
        else
        {
            await Server.CallListener(this, packet, payload);
        }
    }

    protected override long GetServerTime() => Server.ServerTime;

    private class WorldPacket
    {
        public NetworkPacketType Type { get; set; }
        public Packet? Payload { get; set; }
    }

    #region Filters

    private readonly WorldSessionFilter _worldSessionFilter;
    private readonly MapSessionFilter _worldMapFilter;

    #endregion

    #region Callback Processing

    public void EnqueueContinuation<T>(Task<T> task, Action<T> callback)
    {
        Task<object> wrappedTask =
            task.ContinueWith(t => (object)t.Result, TaskContinuationOptions.ExecuteSynchronously)!;
        Action<object> wrappedCallback = result => callback((T)result);

        _genericTaskQueue.Enqueue((wrappedTask, wrappedCallback));
    }

    public void EnqueueContinuation(Task task, Action callback) => _taskQueue.Enqueue((task, callback));

    private void ProcessContinuations()
    {
        while (_taskQueue.TryDequeue(out (Task, Action) item))
        {
            (Task task, var callback) = item;
            if (task.IsCompleted)
            {
                callback?.Invoke();
            }
            else
            {
                // Re-enqueue the task if it is not completed yet
                _taskQueue.Enqueue(item);
            }
        }

        while (_genericTaskQueue.TryDequeue(out (Task<object>, Action<object>) item))
        {
            (Task<object> task, var callback) = item;
            if (task.IsCompleted)
            {
                if (task.IsCompletedSuccessfully)
                {
                    callback?.Invoke(task.Result);
                }
                else
                {
                    // Handle errors
                    _logger.LogError(task?.Exception, "Error processing task: {ExceptionMessage}",
                        task?.Exception?.Message);
                }
            }
            else
            {
                // Re-enqueue the task if it is not completed yet
                _genericTaskQueue.Enqueue(item);
            }
        }
    }

    #endregion
}
