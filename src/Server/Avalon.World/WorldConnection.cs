using System.Collections.Concurrent;
using System.Net.Sockets;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.Threading;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Hosting;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.Movement;
using Avalon.World.Entities;
using Avalon.World.Filters;
using Avalon.World.Maps;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Spells;
using Microsoft.Extensions.Logging;
using Packet = Avalon.Network.Packets.Packet;

namespace Avalon.World;

public class WorldConnection : Connection, IWorldConnection
{
    private class WorldPacket
    {
        public NetworkPacketType Type { get; set; }
        public Packet? Payload { get; set; }
    }

    private readonly IWorldServer _server;
    public AccountId? AccountId { get; set; }

    public ICharacter? Character
    {
        get => _characterEntity;
        set
        {
            _characterEntity = value as CharacterEntity;
        }
    }
    public long Latency { get; private set; }
    public long RoundTripTime { get; private set; }
    public bool InGame => Character != null;
    public bool InMap => InGame && _characterEntity?.Map > 0;

    private CharacterEntity? _characterEntity;

    private long _lastClientTicks = 0;
    private long _lastServerTicks = 0;
    private long _timeSyncOffset = 0;

    #region Filters

    private readonly WorldSessionFilter _worldSessionFilter;
    private readonly MapSessionFilter _worldMapFilter;

    #endregion

    private readonly LockedQueue<WorldPacket> _receiveQueue;
    private readonly ConcurrentQueue<(Task<object>, Action<object>)> _genericTaskQueue = new();
    private readonly ConcurrentQueue<(Task, Action)> _taskQueue = new();

    public WorldConnection(IWorldServer server, TcpClient client, ILoggerFactory loggerFactory, IPacketReader packetReader)
        : base(loggerFactory.CreateLogger<WorldConnection>(), (server as IServerBase)!, packetReader)
    {
        _server = server;
        _receiveQueue = new LockedQueue<WorldPacket>();
        _worldSessionFilter = new WorldSessionFilter(this);
        _worldMapFilter = new MapSessionFilter(this);
        Init(client);
    }

    #region Callback Processing

    public void AddQueryCallback<T>(Task<T> task, Action<T> callback)
    {
        Task<object> wrappedTask = task.ContinueWith(t => (object)t.Result, TaskContinuationOptions.ExecuteSynchronously)!;
        Action<object> wrappedCallback = result => callback((T)result);

        _genericTaskQueue.Enqueue((wrappedTask, wrappedCallback));
    }

    public void AddQueryCallback(Task task, Action callback)
    {
        _taskQueue.Enqueue((task, callback));
    }

    public void ProcessQueryCallbacks()
    {
        while (_taskQueue.TryDequeue(out var item))
        {
            var (task, callback) = item;
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

        while (_genericTaskQueue.TryDequeue(out var item))
        {
            var (task, callback) = item;
            if (task.IsCompleted)
            {
                if (task.IsCompletedSuccessfully)
                {
                    callback?.Invoke(task.Result);
                }
                else
                {
                    // Handle errors
                    _logger.LogError($"Error processing task: {task.Exception}");
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

    public void EnableTimeSyncWorker()
    {
        _ = Task.Factory.StartNew(TimeSyncWorker, CancellationTokenSource!.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public void OnPongReceived(long lastServerTimestamp, long clientReceivedTimestamp, long clientSentTimestamp)
    {
        var rtt = clientReceivedTimestamp - lastServerTimestamp + (DateTime.UtcNow.Ticks - clientSentTimestamp);
        var latency = rtt / TimeSpan.TicksPerMillisecond;

        _timeSyncOffset = _lastServerTicks + (rtt / 2) - _lastClientTicks;

        if (Latency - latency > 20)
        {
            _logger.LogInformation("[{CharName}] Latency changed: {Latency}ms -> {NewLatency}ms", Character?.Name ?? AccountId?.ToString(), Latency, latency);
        }

        _logger.LogTrace("[{CharName}] RTT: {Rtt}ticks, Latency: {Latency}ms", Character?.Name ?? AccountId?.ToString(), rtt, latency);

        _lastClientTicks = clientReceivedTimestamp;
        RoundTripTime = rtt;
        Latency = latency;
    }


    public void Update(TimeSpan deltaTime, PacketFilter filter)
    {
        const uint MaxPacketsPerUpdate = 150;
        uint processedPackets = 0;
        List<WorldPacket> requeuePackets = [];

        WorldPacket? packet = null;

        while (IsConnected && _receiveQueue.Next(out packet, worldPacket => filter.CanProcess(worldPacket.Type)))
        {
            if (packet == null)
            {
                _logger.LogWarning("Received null packet");
                break;
            }

            try
            {
                if (_server.PacketHandlers.TryGetValue(packet.Type, out var handler))
                {
                    handler.Execute(this, packet.Payload!);
                }
                else
                {
                    _logger.LogWarning("No handler for packet {PacketType}", packet.Type);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing packet {PacketType}", packet.Type);
            }

            processedPackets++;
            if (processedPackets > MaxPacketsPerUpdate)
            {
                break;
            }
        }

        _receiveQueue.Readd(requeuePackets);

        ProcessQueryCallbacks();
    }

    public double LastMovementTime { get; set; }

    private async Task TimeSyncWorker()
    {
        var firstIteration = true;
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

    protected override void OnHandshakeFinished()
    {
        Server.CallConnectionListener(this);
    }

    protected override Task<Stream> GetStream(TcpClient client)
    {
        return Task.FromResult<Stream>(new NetworkStream(client.Client, true));
    }

    protected override async Task OnClose(bool expected = true)
    {
        await (Server as WorldServer)!.World.DespawnPlayerAsync(this);
        await Server.RemoveConnection(this);
    }

    protected override async Task OnReceive(NetworkPacket packet, Packet? payload)
    {
        if (_worldSessionFilter.CanProcess(packet.Header.Type) || _worldMapFilter.CanProcess(packet.Header.Type))
        {
            _receiveQueue.Add(new WorldPacket { Type = packet.Header.Type, Payload = payload });
        }
        else
        {
            await Server.CallListener(this, packet, payload);
        }
    }

    protected override long GetServerTime()
    {
        return Server.ServerTime;
    }
}
