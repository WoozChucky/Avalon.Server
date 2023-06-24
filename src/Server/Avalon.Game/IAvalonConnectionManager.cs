using System.Collections.Concurrent;
using Avalon.Network.Abstractions;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Generic;
using Microsoft.Extensions.Logging;

namespace Avalon.Game;

public interface IAvalonConnectionManager : IDisposable
{
    void Start();
    
    void Stop();
    
    event PlayerConnectedHandler? PlayerConnected;
    event PlayerDisconnectedHandler? PlayerDisconnected;
    event PlayerTimedOutHandler? PlayerTimedOut;
    event PlayerReconnectedHandler? PlayerReconnected;
    
    Task AddConnection(IRemoteSource source, CWelcomePacket packet);
    Task HandlePongPacket(IRemoteSource source, CPongPacket packet);
}

public delegate void PlayerConnectedHandler(object? sender, AvalonConnection connection);
public delegate void PlayerDisconnectedHandler(object? sender, AvalonConnection connection);
public delegate void PlayerTimedOutHandler(object? sender, AvalonConnection connection);
public delegate void PlayerReconnectedHandler(object? sender, AvalonConnection connection);

public class AvalonConnectionManager : IAvalonConnectionManager
{
    public event PlayerConnectedHandler? PlayerConnected;
    public event PlayerDisconnectedHandler? PlayerDisconnected;
    public event PlayerTimedOutHandler? PlayerTimedOut;
    public event PlayerReconnectedHandler? PlayerReconnected;
    
    private readonly ILogger<AvalonConnectionManager> _logger;
    private readonly ConcurrentDictionary<Guid, AvalonConnection> _connections;
    private readonly CancellationTokenSource _cancellationTokenSource;
    
    private const int MonitorInterval = 100;
    private const int PingInterval = 2500;
    private const int PingTimeoutThreshold = 10000;
    private const int PingTimeoutThresholdInSec = PingTimeoutThreshold / 1000;
    private const int PingDisconnectThreshold = 30000;
    private const int PingDisconnectThresholdInSec = PingDisconnectThreshold / 1000;
    

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
        
        _logger.LogInformation("Connection manager started");
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
                await Task.Delay(MonitorInterval, _cancellationTokenSource.Token);
                
                foreach (var connection in _connections.Values)
                {
                    if (connection.Status == ConnectionStatus.Connected)
                    {
                        // check if the connection has timed out
                        if (connection.RoundTripTime > PingTimeoutThreshold || DateTime.UtcNow - connection.LastUpdateAt > TimeSpan.FromSeconds(PingTimeoutThresholdInSec))
                        {
                            _logger.LogInformation("Client {Id} has timed out", connection.Id);
                            connection.Status = ConnectionStatus.TimedOut;
                            connection.LastUpdateAt = DateTime.UtcNow;
                            connection.RoundTripTime = PingTimeoutThreshold;
                            try
                            {
                                PlayerTimedOut?.Invoke(this, connection);
                            }
                            catch (Exception e)
                            {
                                _logger.LogWarning(e,"Handler of PlayerTimedOut event for client {Id} threw.", connection.Id);
                            }
                        }
                    }
                    else if (connection.Status == ConnectionStatus.TimedOut)
                    {
                        // check if the connection has reconnected
                        if (connection.RoundTripTime < PingTimeoutThreshold && DateTime.UtcNow - connection.LastUpdateAt < TimeSpan.FromSeconds(PingTimeoutThresholdInSec))
                        {
                            _logger.LogInformation("Client {Id} has reconnected", connection.Id);
                            connection.Status = ConnectionStatus.Connected;
                            connection.LastUpdateAt = DateTime.UtcNow;
                            
                            try
                            {
                                PlayerReconnected?.Invoke(this, connection);
                            }
                            catch (Exception e)
                            {
                                _logger.LogWarning(e,"Handler of PlayerReconnected event for client {Id} threw.", connection.Id);
                            }
                            
                            // Resend all queued packets
                            await connection.SendQueuedPacketsAsync();
                        }
                        else if (connection.RoundTripTime > PingDisconnectThreshold || DateTime.UtcNow - connection.LastUpdateAt > TimeSpan.FromSeconds(PingDisconnectThresholdInSec))
                        {
                            _logger.LogInformation("Client {Id} has disconnected", connection.Id);
                            connection.Status = ConnectionStatus.Disconnected;
                            
                            try
                            {
                                PlayerDisconnected?.Invoke(this, connection);
                            }
                            catch (Exception e)
                            {
                                _logger.LogWarning(e,"Handler of PlayerDisconnected event for client {Id} threw.", connection.Id);
                            }

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
                await Task.Delay(PingInterval, _cancellationTokenSource.Token);
                
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
                            _logger.LogWarning(e, "Failed to send ping packet to client {Id}", connection.Id);
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
                try
                {
                    PlayerConnected?.Invoke(this, connection);
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e,"Handler of PlayerConnected event for client {Id} threw.", connection.Id);
                }
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
        _cancellationTokenSource.Dispose();
        foreach (var connection in _connections.Values)
        {
            connection.Dispose();
        }
    }
}
