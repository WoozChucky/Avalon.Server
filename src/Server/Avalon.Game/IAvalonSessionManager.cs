using System.Collections.Concurrent;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Network;
using Avalon.Network.Packets.Generic;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto;

namespace Avalon.Game;

public interface IAvalonSessionManager : IDisposable
{
    void Start();
    
    void Stop();
    
    event SessionLostHandler? SessionLost;
    event SessionTimedOutHandler? SessionTimedOut;
    event SessionReconnectedHandler? SessionReconnected;
    
    Task HandlePongPacket(IRemoteSource source, CPongPacket packet);
    void RemoveConnection(IRemoteSource source);
    void AddSession(IRemoteSource source, AsymmetricCipherKeyPair serverKeyPair, byte[] clientPublicKey);
    bool PatchSession(IRemoteSource source, AccountId accountId);
    AvalonWorldSession? GetSession(AccountId accountId);
    AvalonWorldSession? GetSession(IRemoteSource source);
    SemaphoreSlim GetSessionLock(AvalonWorldSession session);
    ConcurrentDictionary<AccountId, AvalonWorldSession> GetSessions();
    ICollection<AvalonWorldSession> GetInGameSessions();
}

public delegate void SessionConnectedHandler(object? sender, AvalonWorldSession session);
public delegate void SessionLostHandler(object? sender, AvalonWorldSession session);
public delegate void SessionTimedOutHandler(object? sender, AvalonWorldSession session);
public delegate void SessionReconnectedHandler(object? sender, AvalonWorldSession session);

public class AvalonSessionManager : IAvalonSessionManager
{
    
    public event SessionLostHandler? SessionLost;
    public event SessionTimedOutHandler? SessionTimedOut;
    public event SessionReconnectedHandler? SessionReconnected;
    
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<AvalonSessionManager> _logger;
    private readonly ConcurrentDictionary<AccountId, AvalonWorldSession> _sessions;
    private readonly ConcurrentDictionary<string, AvalonWorldSession> _handshakingSessions;
    private readonly ConcurrentDictionary<AvalonWorldSession, SemaphoreSlim> _sessionLocks;
    private readonly CancellationTokenSource _cts;
    
    private const int MonitorInterval = 100;
    private const int PingInterval = 15000;
    private const int PingTimeoutThreshold = 20000;
    private const int PingTimeoutThresholdInSec = PingTimeoutThreshold / 1000;
    private const int PingDisconnectThreshold = 30000;
    private const int PingDisconnectThresholdInSec = PingDisconnectThreshold / 1000;

    public AvalonSessionManager(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<AvalonSessionManager>();
        _sessions = new ConcurrentDictionary<AccountId, AvalonWorldSession>();
        _handshakingSessions = new ConcurrentDictionary<string, AvalonWorldSession>();
        _sessionLocks = new ConcurrentDictionary<AvalonWorldSession, SemaphoreSlim>();
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
                        if (connection.Latency > PingTimeoutThreshold || DateTime.UtcNow - connection.LastUpdateAt > TimeSpan.FromSeconds(PingTimeoutThresholdInSec))
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
                        if (connection.Latency < PingTimeoutThreshold && DateTime.UtcNow - connection.LastUpdateAt < TimeSpan.FromSeconds(PingTimeoutThresholdInSec))
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
                        else if (connection.Latency > PingDisconnectThreshold || DateTime.UtcNow - connection.LastUpdateAt > TimeSpan.FromSeconds(PingDisconnectThresholdInSec))
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
    
    public void AddSession(IRemoteSource source, AsymmetricCipherKeyPair serverKeyPair, byte[] clientPublicKey)
    {
        foreach (var session in _sessions.Values)
        {
            if (session.Connection?.RemoteAddress == source.RemoteAddress)
            {
                _logger.LogWarning("Client {Id} already connected", session.AccountId);
                return;
            }
        }

        foreach (var handshakingSession in _handshakingSessions)
        {
            if (handshakingSession.Value.Connection?.RemoteAddress == source.RemoteAddress)
            {
                _logger.LogWarning("Client {Id} already handshaking", handshakingSession.Value.AccountId);
                return;
            }
        }
        
        var connectingSession = new AvalonWorldSession(_loggerFactory, source, serverKeyPair, clientPublicKey);
        connectingSession.Status = ConnectionStatus.Handshake;
        
        if (!_handshakingSessions.TryAdd(source.RemoteAddress, connectingSession))
        {
            _logger.LogWarning("Failed to add client {Id} to handshaking sessions", connectingSession.AccountId);
            return;
        }
    }

    public bool PatchSession(IRemoteSource source, AccountId accountId)
    {
        var session = _handshakingSessions
            .Values
            .FirstOrDefault(handshakingSession => handshakingSession.Connection!.RemoteAddress == source.RemoteAddress);

        if (session == null)
        {
            _logger.LogWarning("Failed to patch session. Account {Id} not found", accountId);
            return false;
        }
        
        if (!_handshakingSessions.TryRemove(session.Connection!.RemoteAddress, out _))
        {
            _logger.LogWarning("Failed to remove client {Id} from handshaking sessions", session.AccountId);
            return false;
        }
        
        session.Status = ConnectionStatus.Connected;
        session.AccountId = accountId;

        _sessions.TryAdd(accountId, session);
        
        return true;
    }

    public AvalonWorldSession? GetSession(AccountId accountId)
    {
        return _sessions.GetValueOrDefault(accountId);
    }

    public AvalonWorldSession? GetSession(IRemoteSource source)
    {
        return source switch
        {
            TcpClient _ => _sessions.Values.FirstOrDefault(c => c.Connection?.RemoteAddress == source.RemoteAddress) 
                           ?? _handshakingSessions.Values.FirstOrDefault(c => c.Connection?.RemoteAddress == source.RemoteAddress),
            UdpClientPacket _ => throw new NotSupportedException("UDP is not supported yet"),
            _ => null
        };
    }

    public SemaphoreSlim GetSessionLock(AvalonWorldSession session)
    {
        return _sessionLocks.GetOrAdd(session, new SemaphoreSlim(1, 1));
    }

    public ConcurrentDictionary<AccountId, AvalonWorldSession> GetSessions()
    {
        return _sessions;
    }

    public ICollection<AvalonWorldSession> GetInGameSessions()
    {
        return _sessions.Where(s => s.Value.InGame).Select(s => s.Value).ToList();
    }

    public Task HandlePongPacket(IRemoteSource source, CPongPacket packet)
    {
        var session = GetSession(source);
        if (session == null)
        {
            _logger.LogWarning("Received pong packet from unknown client {Id}", source.RemoteAddress);
            return Task.CompletedTask;
        }

        session.OnPong(packet.LastServerTimestamp, packet.ClientReceivedTimestamp, packet.ClientSentTimestamp);
        
        return Task.CompletedTask;
    }

    public void RemoveConnection(IRemoteSource source)
    {
        AvalonWorldSession? session = null;
        
        if (source is TcpClient)
        {
            session = _sessions.Values.FirstOrDefault(c => c.Connection?.RemoteAddress == source.RemoteAddress);
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
            
            session.Dispose();
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
