using System.Collections.Concurrent;
using Avalon.Network.Abstractions;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Generic;
using Microsoft.Extensions.Logging;

namespace Avalon.Game;

public enum ConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    TimedOut,
}

public class AvalonConnection : IDisposable
{
    // Since this connection reference will be shared against multiple threads, we need to make sure
    // that the properties are thread-safe. We can do this by making them read-only and setting them
    // in the constructor. But also when we need to update them, we need to make sure that we are
    // using the Interlocked class to update them or the Volatile methods. I think ??
    
    public Guid Id { get; private set; }
    public IRemoteSource? Udp { get; private set; }
    public IRemoteSource? Tcp { get; private set; }
    public long RoundTripTime { get; set; } // in milliseconds
    public ConnectionStatus Status { get; set; }
    public DateTime LastUpdateAt { get; set; } = DateTime.UtcNow;

    private readonly ConcurrentQueue<NetworkPacket> _packetQueue;
    
    private long _lastTicks;
    
    private long _sequenceNumber = 0;
    
    public AvalonConnection(Guid id)
    {
        Id = id;
        _packetQueue = new ConcurrentQueue<NetworkPacket>();
        Status = ConnectionStatus.Connecting;
    }
    
    public async Task PingAsync()
    {
        _lastTicks = DateTime.UtcNow.Ticks;
        var sequenceNumber = Interlocked.Increment(ref _sequenceNumber);
        var packet = SPingPacket.Create(sequenceNumber, _lastTicks);
        await SendAsync(packet);
    }
    
    public void OnPong(long sequenceNumber, long ticks)
    {
        if (sequenceNumber == _sequenceNumber)
        {
            LastUpdateAt = DateTime.UtcNow;
            var now = DateTime.UtcNow.Ticks;
            RoundTripTime = (now - ticks) / TimeSpan.TicksPerMillisecond;
            Console.WriteLine($"Round trip time: {RoundTripTime}ms");
        }
    }

    public async Task SendAsync(NetworkPacket packet)
    {
        // We might have lost connection which means the SendAsync will throw,
        // in this case, we should store the packet in a queue and then send it
        // once we have reconnected (handled by ping/pong or if could not reconnect in time).
        
        try
        {
            switch (packet.Header.Protocol)
            {
                case NetworkProtocol.Tcp:
                    Tcp?.SendAsync(packet);
                    break;
                case NetworkProtocol.Udp:
                    Udp?.SendAsync(packet);
                    break;
                case NetworkProtocol.Both:
                    Tcp?.SendAsync(packet);
                    Udp?.SendAsync(packet);
                    break;
                default:
                case NetworkProtocol.None:
                    throw new InvalidOperationException("Cannot send a packet with no protocol specified.");
            }
        }
        catch (IOException e)
        {
            _packetQueue.Enqueue(packet);
        }
    }
    
    public async Task SendQueuedPacketsAsync()
    {
        while (_packetQueue.Count > 0)
        {
            if (_packetQueue.TryDequeue(out var packet))
            {
                await SendAsync(packet);
            }
        }
    }
    
    internal void SetUdp(UdpClientPacket udp)
    {
        Udp = udp;
        if (Tcp != null)
            Status = ConnectionStatus.Connected;
    }
    
    internal void SetTcp(TcpClient tcp)
    {
        Tcp = tcp;
        if (Udp != null)
            Status = ConnectionStatus.Connected;
    }

    public void Dispose()
    {
        Tcp?.Dispose();
        Udp?.Dispose();
    }
}

public interface IAvalonConnectionManager : IDisposable
{
    void Start();
    
    void Stop();
    
    event PlayerConnectedHandler PlayerConnected;
    event PlayerDisconnectedHandler PlayerDisconnected;
    event PlayerTimedOutHandler PlayerTimedOut;
    event PlayerReconnectedHandler PlayerReconnected;
    
    Task AddConnection(IRemoteSource source, CWelcomePacket packet);
    Task HandlePongPacket(IRemoteSource source, CPongPacket packet);
}

public delegate void PlayerConnectedHandler(object? sender, AvalonConnection connection);
public delegate void PlayerDisconnectedHandler(object? sender, AvalonConnection connection);
public delegate void PlayerTimedOutHandler(object? sender, AvalonConnection connection);
public delegate void PlayerReconnectedHandler(object? sender, AvalonConnection connection);

public class AvalonConnectionManager : IAvalonConnectionManager
{
    public event PlayerConnectedHandler PlayerConnected;
    public event PlayerDisconnectedHandler PlayerDisconnected;
    public event PlayerTimedOutHandler PlayerTimedOut;
    public event PlayerReconnectedHandler PlayerReconnected;
    
    private readonly ILogger<AvalonConnectionManager> _logger;
    private readonly ConcurrentDictionary<Guid, AvalonConnection> _connections;
    private readonly CancellationTokenSource _cancellationTokenSource;

    public AvalonConnectionManager(ILogger<AvalonConnectionManager> logger)
    {
        _logger = logger;
        _connections = new ConcurrentDictionary<Guid, AvalonConnection>();
        _cancellationTokenSource = new CancellationTokenSource();
    }
    
    public void Start()
    {
        Task.Run(StartMonitoringConnections);
        Task.Run(StartPingPongWorker);
    }

    public void Stop()
    {
        _cancellationTokenSource?.Cancel();
    }
    
    private async Task StartMonitoringConnections()
    {
        try
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                // implement logic to check for timed out connections
                await Task.Delay(100, _cancellationTokenSource.Token);
                
                foreach (var connection in _connections.Values)
                {
                    if (connection.Status == ConnectionStatus.Connected)
                    {
                        // check if the connection has timed out
                        if (connection.RoundTripTime > 10000 || DateTime.UtcNow - connection.LastUpdateAt > TimeSpan.FromSeconds(10))
                        {
                            _logger.LogInformation("Client {Id} has timed out", connection.Id);
                            connection.Status = ConnectionStatus.TimedOut;
                            connection.LastUpdateAt = DateTime.UtcNow;
                            connection.RoundTripTime = 10000;
                            PlayerTimedOut?.Invoke(this, connection);
                        }
                    }
                    else if (connection.Status == ConnectionStatus.TimedOut)
                    {
                        // check if the connection has reconnected
                        if (connection.RoundTripTime < 10000 && DateTime.UtcNow - connection.LastUpdateAt < TimeSpan.FromSeconds(10))
                        {
                            _logger.LogInformation("Client {Id} has reconnected", connection.Id);
                            connection.Status = ConnectionStatus.Connected;
                            connection.LastUpdateAt = DateTime.UtcNow;
                            PlayerReconnected?.Invoke(this, connection);
                            
                            await connection.SendQueuedPacketsAsync();
                        }
                        else if (connection.RoundTripTime > 30000 || DateTime.UtcNow - connection.LastUpdateAt > TimeSpan.FromSeconds(30))
                        {
                            _logger.LogInformation("Client {Id} has disconnected", connection.Id);
                            connection.Status = ConnectionStatus.Disconnected;
                            PlayerDisconnected?.Invoke(this, connection);
                            
                            //TODO: this might throw because we are iterating over the collection
                            _connections.TryRemove(connection.Id, out _); 
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
    }
    
    private async Task StartPingPongWorker()
    {
        try
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                // implement logic send ping packets to all connected clients
                await Task.Delay(2500, _cancellationTokenSource.Token);
                
                foreach (var connection in _connections.Values)
                {
                    if (connection.Status is ConnectionStatus.Connected or ConnectionStatus.TimedOut)
                    {
                        // send ping packet
                        try
                        {
                            await connection.PingAsync();
                        }
                        catch (Exception e)
                        {
                            _logger.LogWarning("Failed to send ping packet to client {Id}", connection.Id);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
    }
    
    public Task AddConnection(IRemoteSource source, CWelcomePacket packet)
    {
        if (!_connections.TryGetValue(packet.ClientId, out var connection))
        {
            connection = new AvalonConnection(packet.ClientId);
            _connections.TryAdd(packet.ClientId, connection);
        }

        lock (connection)
        {
            switch (source)
            {
                case TcpClient tcp:
                    connection.SetTcp(tcp);
                    break;
                case UdpClientPacket udp:
                    connection.SetUdp(udp);
                    break;
                default:
                    _logger.LogWarning("Unknown connection type {Type}", source.GetType());
                    break;
            }

            if (connection.Status == ConnectionStatus.Connected)
            {
                _logger.LogInformation("Client {Id} has connected from {Ip}", connection.Id, connection.Tcp?.RemoteAddress);
                PlayerConnected?.Invoke(this, connection);
            }
        }

        return Task.CompletedTask;
    }

    public Task HandlePongPacket(IRemoteSource source, CPongPacket packet)
    {
        if (!_connections.TryGetValue(packet.ClientId, out var connection))
        {
            _logger.LogWarning("Received pong packet from unknown client {Id}", packet.ClientId);
            return Task.CompletedTask;
        }

        connection.OnPong(packet.SequenceNumber, packet.Ticks);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Dispose();
        foreach (var connection in _connections.Values)
        {
            connection.Dispose();
        }
    }
}
