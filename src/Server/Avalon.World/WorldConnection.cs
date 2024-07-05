using System.Net.Sockets;
using Avalon.Domain.Auth;
using Avalon.Domain.Characters;
using Avalon.Hosting;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;
using Microsoft.Extensions.Logging;

namespace Avalon.World;

public interface IWorldConnection : IConnection
{
    public AccountId? AccountId { get; set; }
    public CharacterId? CharacterId { get; set; }
    public Character? Character { get; set; }
    public long Latency { get; }
    public long RoundTripTime { get; }
    
    public bool InGame { get; }
    public bool InMap { get; }
    void EnableTimeSyncWorker();
    void OnPongReceived(long packetLastServerTimestamp, long packetClientReceivedTimestamp, long packetClientSentTimestamp);
}

public class WorldConnection : Connection, IWorldConnection
{
    public AccountId? AccountId { get; set; }
    public CharacterId? CharacterId { get; set; }
    public Character? Character { get; set; }
    public long Latency { get; private set; }
    public long RoundTripTime { get; private set; }
    public bool InGame => Character != null;
    public bool InMap => InGame && Character!.InstanceId != null;
    
    private long _lastClientTicks = 0;
    private long _lastServerTicks = 0;
    private long _timeSyncOffset = 0;

    public WorldConnection(IServerBase server, TcpClient client, ILoggerFactory loggerFactory, 
        PluginExecutor pluginExecutor, IPacketReader packetReader) 
        : base(loggerFactory.CreateLogger<WorldConnection>(), server, pluginExecutor, packetReader)
    {
        Init(client);
    }
    
    public void EnableTimeSyncWorker()
    {
        _ = Task.Factory.StartNew(TimeSyncWorker, CancellationTokenSource!.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public void OnPongReceived(long lastServerTimestamp, long clientReceivedTimestamp, long clientSentTimestamp)
    {
        var rtt = clientReceivedTimestamp - lastServerTimestamp + (DateTime.UtcNow.Ticks - clientSentTimestamp);
        var latency = rtt / TimeSpan.TicksPerMillisecond;
        
        _timeSyncOffset = _lastServerTicks + (rtt / 2) - _lastClientTicks;
        
        if (Latency - latency >  20)
        {
            _logger.LogInformation("[{CharName}] Latency changed: {Latency}ms -> {NewLatency}ms", Character?.Name ?? AccountId?.ToString(), Latency, latency);
        }
        
        _logger.LogTrace("[{CharName}] RTT: {Rtt}ticks, Latency: {Latency}ms", Character?.Name ?? AccountId?.ToString(), rtt, latency);
        
        _lastClientTicks = clientReceivedTimestamp;
        RoundTripTime = rtt;
        Latency = latency;
    }

    private async Task TimeSyncWorker()
    {
        var firstIteration = true;
        try
        {
            while (!CancellationTokenSource!.Token.IsCancellationRequested)
            {
                _lastServerTicks = DateTime.UtcNow.Ticks;
                
                Send(SPingPacket.Create(_lastServerTicks, _lastClientTicks, RoundTripTime, _timeSyncOffset));
            
                await Task.Delay(TimeSpan.FromSeconds(firstIteration ? 2 : 15), CancellationTokenSource!.Token);
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
        await Server.RemoveConnection(this);
    }

    protected override async Task OnReceive(NetworkPacket packet, Packet? payload)
    {
        await Server.CallListener(this, packet, payload);
    }

    protected override long GetServerTime()
    {
        return Server.ServerTime;
    }
}
