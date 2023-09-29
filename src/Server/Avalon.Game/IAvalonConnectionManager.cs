using System.Collections;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Avalon.Network;
using Avalon.Network.Packets.Generic;
using Microsoft.Extensions.Logging;

namespace Avalon.Game;

public interface IAvalonConnectionManager : IDisposable
{
    void Start();
    
    void Stop();
    
    event SessionLostHandler? SessionLost;
    event SessionTimedOutHandler? SessionTimedOut;
    event SessionReconnectedHandler? SessionReconnected;
    
    Task HandlePongPacket(IRemoteSource source, CPongPacket packet);
    void RemoveConnection(IRemoteSource source);
    void AddSession(IRemoteSource source, int accountId, byte[] privateKey);
    bool PatchSession(IRemoteSource source, int accountId, byte[] privateKey);
    AvalonSession? GetSession(int accountId);
    AvalonSession? GetSession(IRemoteSource source);
    ConcurrentDictionary<int, AvalonSession> GetSessions();
    ICollection<AvalonSession> GetInGameSessions();
}

public delegate void SessionConnectedHandler(object? sender, AvalonSession session);
public delegate void SessionLostHandler(object? sender, AvalonSession session);
public delegate void SessionTimedOutHandler(object? sender, AvalonSession session);
public delegate void SessionReconnectedHandler(object? sender, AvalonSession session);

public class AvalonConnectionManager : IAvalonConnectionManager
{
    public event SessionLostHandler? SessionLost;
    public event SessionTimedOutHandler? SessionTimedOut;
    public event SessionReconnectedHandler? SessionReconnected;
    
    private readonly ILogger<AvalonConnectionManager> _logger;
    private readonly ConcurrentDictionary<int, AvalonSession> _sessions;
    private readonly CancellationTokenSource _cts;
    
    private const int MonitorInterval = 100;
    private const int PingInterval = 2500;
    private const int PingTimeoutThreshold = 10000;
    private const int PingTimeoutThresholdInSec = PingTimeoutThreshold / 1000;
    private const int PingDisconnectThreshold = 30000;
    private const int PingDisconnectThresholdInSec = PingDisconnectThreshold / 1000;

    public AvalonConnectionManager(ILogger<AvalonConnectionManager> logger)
    {
        _logger = logger;
        _sessions = new ConcurrentDictionary<int, AvalonSession>();
        _cts = new CancellationTokenSource();
    }
    
    public void Start()
    {
        Task.Run(StartMonitoringConnections);
        Task.Run(StartPingPongWorker);
        
        _logger.LogInformation("Connection manager started");
    }

    public void Stop()
    {
        _logger.LogInformation("Connection manager stopped");
        _cts?.Cancel();
    }
    
    private async Task StartMonitoringConnections()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                // implement logic to check for timed out connections
                await Task.Delay(MonitorInterval, _cts.Token);
                
                foreach (var connection in _sessions.Values)
                {
                    if (connection.Status == ConnectionStatus.Connected)
                    {
                        // check if the connection has timed out
                        if (connection.RoundTripTime > PingTimeoutThreshold && DateTime.UtcNow - connection.LastUpdateAt > TimeSpan.FromSeconds(PingTimeoutThresholdInSec))
                        {
                            _logger.LogInformation("Account {Id} has timed out", connection.AccountId);
                            connection.Status = ConnectionStatus.TimedOut;
                            connection.LastUpdateAt = DateTime.UtcNow;
                            try
                            {
                                SessionTimedOut?.Invoke(this, connection);
                            }
                            catch (Exception e)
                            {
                                _logger.LogWarning(e,"Handler of SessionTimedOut event for Account {Id} threw", connection.AccountId);
                            }
                        }
                    }
                    else if (connection.Status == ConnectionStatus.TimedOut)
                    {
                        // check if the connection has reconnected
                        if (connection.RoundTripTime < PingTimeoutThreshold && DateTime.UtcNow - connection.LastUpdateAt < TimeSpan.FromSeconds(PingTimeoutThresholdInSec))
                        {
                            _logger.LogInformation("Account {Id} has reconnected", connection.AccountId);
                            connection.Status = ConnectionStatus.Connected;
                            connection.LastUpdateAt = DateTime.UtcNow;
                            
                            try
                            {
                                SessionReconnected?.Invoke(this, connection);
                            }
                            catch (Exception e)
                            {
                                _logger.LogWarning(e,"Handler of SessionReconnected event for Account {Id} threw", connection.AccountId);
                            }
                            
                            // Resend all queued packets
                            // await connection.SendQueuedPacketsAsync();
                        }
                        else if (connection.RoundTripTime > PingDisconnectThreshold || DateTime.UtcNow - connection.LastUpdateAt > TimeSpan.FromSeconds(PingDisconnectThresholdInSec))
                        {
                            _logger.LogInformation("Client {Id} has disconnected", connection.AccountId);
                            connection.Status = ConnectionStatus.Disconnected;
                            
                            try
                            {
                                SessionLost?.Invoke(this, connection);
                            }
                            catch (Exception e)
                            {
                                _logger.LogWarning(e,"Handler of SessionLost event for client {Id} threw", connection.AccountId);
                            }

                            //TODO: this might throw because we are iterating over the collection
                            _sessions.TryRemove(connection.AccountId, out _); 
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
            while (!_cts.IsCancellationRequested)
            {
                // implement logic send ping packets to all connected clients
                await Task.Delay(PingInterval, _cts.Token);
                
                foreach (var connection in _sessions.Values)
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
                            _logger.LogWarning(e, "Failed to send ping packet to client {Id}", connection.AccountId);
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
    
    public void AddSession(IRemoteSource source, int accountId, byte[] privateKey)
    {
        if (!_sessions.TryGetValue(accountId, out var session))
        {
            session = new AvalonSession(accountId);
            session.InitializeCryptography(privateKey);
            _sessions.TryAdd(accountId, session);
        }
        else
        {
            _logger.LogWarning("Account {Id} already connected, updating server side private key", accountId);
            session.InitializeCryptography(privateKey);
        }
        
        session.SetTcp(source as TcpClient ?? throw new InvalidOperationException("Connection is not a TCP connection"));
        session.Status = ConnectionStatus.PendingKey;
    }

    public bool PatchSession(IRemoteSource source, int accountId, byte[] privateKey)
    {
        if (!_sessions.TryGetValue(accountId, out var session))
        {
            _logger.LogWarning("Failed to patch session. Account {Id} not connected", accountId);
            return false;
        }

        // now we compare the private keys
        if (!session.SessionKey.SequenceEqual(privateKey))
        {
            _logger.LogWarning("Failed to patch session. Account {Id} private key mismatch", accountId);
            return false;
        }
        
        session.SetUdp(source as UdpClientPacket ?? throw new InvalidOperationException("Connection is not a UDP connection"));
        session.Status = ConnectionStatus.Connected;

        return true;
    }

    public AvalonSession? GetSession(int accountId)
    {
        return _sessions.TryGetValue(accountId, out var session) ? session : null;
    }

    public AvalonSession? GetSession(IRemoteSource source)
    {
        return source switch
        {
            TcpClient _ => _sessions.Values.FirstOrDefault(c => c.Tcp?.RemoteAddress == source.RemoteAddress),
            UdpClientPacket _ => _sessions.Values.FirstOrDefault(c => c.Udp?.RemoteAddress == source.RemoteAddress),
            _ => null
        };
    }

    public ConcurrentDictionary<int, AvalonSession> GetSessions()
    {
        return _sessions;
    }

    public ICollection<AvalonSession> GetInGameSessions()
    {
        return _sessions.Where(s => s.Value.InGame).Select(s => s.Value).ToList();
    }

    public Task HandlePongPacket(IRemoteSource source, CPongPacket packet)
    {
        /*
        if (!_sessions.TryGetValue(packet.ClientId, out var connection))
        {
            _logger.LogWarning("Received pong packet from unknown client {Id}", packet.ClientId);
            return Task.CompletedTask;
        }

        connection.OnPong(packet.SequenceNumber, packet.Ticks);
        */
        
        return Task.CompletedTask;
    }

    public void RemoveConnection(IRemoteSource source)
    {
        AvalonSession? session = null;
        
        if (source is TcpClient tcp)
        {
            session = _sessions.Values.FirstOrDefault(c => c.Tcp?.RemoteAddress == source.RemoteAddress);
        }
        else if (source is UdpClientPacket udp)
        {
            session = _sessions.Values.FirstOrDefault(c => c.Udp?.RemoteAddress == source.RemoteAddress);
        }
        
        if (session == null)
        {
            _logger.LogWarning("Received disconnect packet from unknown client {ConnectionId}", session?.AccountId);
            return;
        }

        session.Status = ConnectionStatus.Disconnected;
        try
        {
            SessionLost?.Invoke(this, session);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e,"Handler of PlayerDisconnected event for client {Id} threw", session.AccountId);
        }

        if (_sessions.TryRemove(session.AccountId, out _))
        {
            _logger.LogInformation("Client {Id} has disconnected", session.AccountId);
        }
    }

    public void Dispose()
    {
        _cts.Dispose();
        foreach (var connection in _sessions.Values)
        {
            connection.Dispose();
        }
    }
}
