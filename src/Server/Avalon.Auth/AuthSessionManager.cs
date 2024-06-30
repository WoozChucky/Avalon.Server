using System.Collections.Concurrent;
using Avalon.Common.Cryptography;
using Avalon.Infrastructure;
using Avalon.Network;
using Avalon.Network.Packets.Generic;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto;

namespace Avalon.Auth;

public interface IAuthSessionManager : IDisposable
{
    void AddSession(IRemoteSource source, AsymmetricCipherKeyPair serverKeyPair, byte[] clientPublicKey);
    bool PatchSession(IRemoteSource source, int accountId);
    AvalonAuthSession? GetSession(int accountId);
    AvalonAuthSession? GetSession(IRemoteSource source);
    SemaphoreSlim GetSessionLock(AvalonAuthSession session);
    ConcurrentDictionary<int, AvalonAuthSession> GetSessions();
    void RemoveConnection(IRemoteSource source);
    void RemoveConnectionAbrupty(IRemoteSource source);
}

public class AuthSessionManager : IAuthSessionManager
{
    
    private readonly ConcurrentDictionary<int, AvalonAuthSession> _sessions;
    private readonly ConcurrentDictionary<string, AvalonAuthSession> _handshakingSessions;
    private readonly ConcurrentDictionary<AvalonAuthSession, SemaphoreSlim> _sessionLocks;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<AuthSessionManager> _logger;
    
    public AuthSessionManager(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<AuthSessionManager>();
        _sessions = new ConcurrentDictionary<int, AvalonAuthSession>();
        _handshakingSessions = new ConcurrentDictionary<string, AvalonAuthSession>();
        _sessionLocks = new ConcurrentDictionary<AvalonAuthSession, SemaphoreSlim>();
    }
    
    public void AddSession(IRemoteSource source, AsymmetricCipherKeyPair serverKeyPair, byte[] clientPublicKey)
    {
        foreach (var session in _sessions.Values)
        {
            if (session.Connection.RemoteAddress == source.RemoteAddress)
            {
                _logger.LogWarning("Client {Id} already connected", session.AccountId);
                return;
            }
        }

        foreach (var handshakingSession in _handshakingSessions)
        {
            if (handshakingSession.Value.Connection.RemoteAddress == source.RemoteAddress)
            {
                _logger.LogWarning("Client {Id} already handshaking", handshakingSession.Value.AccountId);
                return;
            }
        }
        
        var connectingSession = new AvalonAuthSession(_loggerFactory, source, serverKeyPair, clientPublicKey);
        connectingSession.Status = ConnectionStatus.Handshake;
        
        if (!_handshakingSessions.TryAdd(source.RemoteAddress, connectingSession))
        {
            _logger.LogWarning("Failed to add client {Id} to handshaking sessions", connectingSession.AccountId);
            return;
        }
    }
    
    public bool PatchSession(IRemoteSource source, int accountId)
    {
        var session = _handshakingSessions
            .Values
            .FirstOrDefault(handshakingSession => handshakingSession.Connection!.RemoteAddress == source.RemoteAddress);

        if (session == null)
        {
            _logger.LogWarning("Failed to patch session. Account {Id} not doesnt match session {Session}", accountId, source.RemoteAddress);
            return false;
        }
        
        if (!_handshakingSessions.TryRemove(session.Connection.RemoteAddress, out _))
        {
            _logger.LogWarning("Failed to remove session {Session} from handshaking sessions", source.RemoteAddress);
            return false;
        }
        
        session.Status = ConnectionStatus.Connected;
        session.AccountId = accountId;

        if (!_sessions.TryAdd(accountId, session))
        {
            _logger.LogWarning("Failed to add account {Id} to active sessions", accountId);
            return false;
        }
        
        return true;
    }
    
    public AvalonAuthSession? GetSession(int accountId)
    {
        return _sessions.GetValueOrDefault(accountId);
    }
    
    public AvalonAuthSession? GetSession(IRemoteSource source)
    {
        return source switch
        {
            TcpClient _ => _sessions.Values.FirstOrDefault(c => c.Connection?.RemoteAddress == source.RemoteAddress) 
                           ?? _handshakingSessions.Values.FirstOrDefault(c => c.Connection?.RemoteAddress == source.RemoteAddress),
            UdpClientPacket _ => throw new NotSupportedException("UDP is not supported yet"),
            _ => null
        };
    }
    
    public SemaphoreSlim GetSessionLock(AvalonAuthSession session)
    {
        return _sessionLocks.GetOrAdd(session, new SemaphoreSlim(1, 1));
    }
    
    public ConcurrentDictionary<int, AvalonAuthSession> GetSessions()
    {
        return _sessions;
    }
    
    public void RemoveConnection(IRemoteSource source)
    {
        AvalonAuthSession? session = null;
        
        session = _sessions.Values.FirstOrDefault(c => c.Connection?.RemoteAddress == source.RemoteAddress);
        
        if (session == null)
        {
            _logger.LogWarning("Disconnect from client ({ConnectionId}) without handshake", source.RemoteAddress);
            return;
        }

        session.Status = ConnectionStatus.Disconnected;

        _logger.LogInformation("Called One time ?");
        
        if (_sessions.TryRemove(session.AccountId, out _))
        {
            _logger.LogInformation("Client {Id} has disconnected", session.AccountId);
            
            session.Dispose();
        }
    }

    public void RemoveConnectionAbrupty(IRemoteSource source)
    {
        AvalonAuthSession? session = null;
        
        session = _sessions.Values.FirstOrDefault(c => c.Connection?.RemoteAddress == source.RemoteAddress);
        
        if (session == null)
        {
            session = _handshakingSessions.Values.FirstOrDefault(c => c.Connection?.RemoteAddress == source.RemoteAddress);
            
            if (session == null)
            {
                _logger.LogWarning("Disconnect from client ({ConnectionId}) without handshake", source.RemoteAddress);
                return;
            }
        }
        
        // TODO: Here exactly

        session.Status = ConnectionStatus.Disconnected;

        _logger.LogInformation("Called One time ?");
        
        if (_sessions.TryRemove(session.AccountId, out _))
        {
            _logger.LogInformation("Client {Id} has disconnected", session.AccountId);
            
            session.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var connection in _sessions.Values)
        {
            connection.Dispose();
        }
    }
}
