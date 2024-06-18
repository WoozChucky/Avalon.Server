using System.Text;
using Avalon.Common.Cryptography;
using Avalon.Database;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;
using Avalon.Network;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Handshake;
using Microsoft.Extensions.Logging;
using OtpNet;

namespace Avalon.Auth;

public class AvalonAuth : IAvalonAuth
{
    private readonly ILogger<AvalonAuth> _logger;
    private readonly CancellationTokenSource _cts;
    private readonly ICryptoManager _cryptography;
    private readonly IDatabaseManager _databaseManager;
    private readonly IAuthSessionManager _sessionManager;
    private readonly IMFAHashService _mfaHashService;
    private readonly IReplicatedCache _cache;
    private volatile bool _isRunning;
    private long _loopCounter;
    
    public AvalonAuth(
        ILoggerFactory loggerFactory, 
        ICryptoManager cryptography, 
        IDatabaseManager databaseManager,
        IReplicatedCache cache,
        IAuthSessionManager sessionManager, 
        IMFAHashService mfaHashService)
    {
        _cryptography = cryptography;
        _databaseManager = databaseManager;
        _sessionManager = sessionManager;
        _mfaHashService = mfaHashService;
        _cache = cache;
        _logger = loggerFactory.CreateLogger<AvalonAuth>();
        _cts = new CancellationTokenSource();
    }
    
    public void Start()
    {
        _logger.LogInformation("Loading auth data");
        
        _isRunning = true;
        
        _logger.LogInformation("Starting auth loop");
    }

    public void Stop()
    {
        _logger.LogInformation("Stopping auth loop");
        _cts.Cancel();
        _isRunning = false;
    }

    public void Update(TimeSpan deltaTime)
    {
        
    }

    public bool IsRunning()
    {
        return _isRunning;
    }

    public void IncrementLoopCounter()
    {
        Interlocked.Increment(ref _loopCounter);
    }

    public long GetLoopCounter()
    {
        Interlocked.Read(ref _loopCounter);
        return _loopCounter;
    }
    
    public async Task HandleServerInfoPacket(IRemoteSource source, CRequestServerInfoPacket packet)
    {
        _logger.LogDebug("Handling server info packet from {EndPoint}", source.RemoteAddress);
        
        if (packet.ClientVersion != "0.0.1") // TODO: Hardcoded client version
        {
            _logger.LogWarning("Client {EndPoint} is using an invalid version", source.RemoteAddress);
            source.Dispose();
            throw new NotImplementedException("Invalid client version not implemented yet");
        }
        
        var result = SServerInfoPacket.Create(
            1_000_000, // TODO: Hardcoded server version
            _cryptography.GetPublicKey()
        );

        await source.SendAsync(result);
    }

    public async Task HandleClientInfoPacket(IRemoteSource source, CClientInfoPacket packet)
    {
        if (packet.PublicKey == null || packet.PublicKey.Length == 0)
        {
            _logger.LogWarning("Client {EndPoint} sent an invalid public key", source.RemoteAddress);
            return;
        }

        if (packet.PublicKey.Length != _cryptography.GetValidKeySize())
        {
            _logger.LogWarning("Client {EndPoint} sent an invalid public key size", source.RemoteAddress);
            return;
        }
        
        _sessionManager.AddSession(source, _cryptography.GetKeyPair(), packet.PublicKey);

        var session = _sessionManager.GetSession(source);
        
        if (session == null)
        {
            _logger.LogWarning("Session not found for client {EndPoint}", source.RemoteAddress);
            return;
        }

        var data = session?.GenerateHandshakeData() ?? throw new InvalidOperationException("Session not found");
        
        var result = SHandshakePacket.Create(data, session.Encrypt);
        
        await source.SendAsync(result);
    }

    public async Task HandleHandshakePacket(IRemoteSource source, CHandshakePacket packet)
    {
        var session = _sessionManager.GetSession(source);
        
        if (session == null)
        {
            _logger.LogWarning("Client {EndPoint} sent a handshake packet, but no session was found", source.RemoteAddress);
            return;
        }
        
        if (!session.VerifyHandshakeData(packet.HandshakeData))
        {
            _logger.LogWarning("Client {EndPoint} sent an invalid handshake data", source.RemoteAddress);
            
            // TODO: Send a packet to the client, and disconnect
            
            return;
        }

        var result = SHandshakeResultPacket.Create(true, session.Encrypt);
        
        await source.SendAsync(result);
    }

    public async Task HandleAuthPacket(IRemoteSource source, CAuthPacket packet)
    {
        _logger.LogDebug("Handling auth packet from {EndPoint}", source.RemoteAddress);
        
        var session = _sessionManager.GetSession(source);

        if (session == null)
        {
            _logger.LogWarning("Session not found for client {EndPoint}", source.RemoteAddress);
            return;
        }
        
        if (string.IsNullOrWhiteSpace(packet.Username) || string.IsNullOrWhiteSpace(packet.Password))
        {
            await session.SendAsync(SAuthResultPacket.Create(null, null, AuthResult.INVALID_CREDENTIALS, session.Encrypt));
            return;
        }

        var account =
            await _databaseManager.Auth.Account.FindByUsernameAsync(packet.Username.ToUpperInvariant().Trim());

        if (account == null)
        {
            await session.SendAsync(SAuthResultPacket.Create(null, null, AuthResult.INVALID_CREDENTIALS, session.Encrypt));
            return;
        }
        
        if (account.Locked)
        {
            await session.SendAsync(SAuthResultPacket.Create(null, null, AuthResult.LOCKED, session.Encrypt));
            return;
        }
        
        var verifier = Encoding.UTF8.GetString(account.Verifier);

        if (!BCrypt.Net.BCrypt.Verify(packet.Password.Trim(), verifier))
        {
            account.LastAttemptIp = source.RemoteAddress.Split(':')[0];
            account.FailedLogins++;
            if (account.FailedLogins >= 5) // TODO: Move this to a configuration
            {
                account.Locked = true;
            }
            
            await _databaseManager.Auth.Account.SaveAsync(account);
            
            await session.SendAsync(SAuthResultPacket.Create(null, null, account.Locked ? AuthResult.LOCKED : AuthResult.INVALID_CREDENTIALS, session.Encrypt));
            return;
        }
        
        var mfa = await _databaseManager.Auth.MFASetup.FindByAccountIdAsync(account.Id!.Value);
        if (mfa is { Status: Status.Confirmed })
        {
            var mfaHash = await _mfaHashService.GenerateHashAsync(account);
            await session.SendAsync(SAuthResultPacket.Create(null, mfaHash, AuthResult.MFA_REQUIRED, session.Encrypt));
            return;
        }
        
        if (account.Online) //TODO: Properly implement this + broadcast to other servers
        {
            var connectedAccount = _sessionManager.GetSession(account.Id!.Value);
            if (connectedAccount != null)
            {
                await connectedAccount.SendAsync(SLogoutPacket.Create(LogoutResult.ConnectedElsewhere, connectedAccount.Encrypt));
                _sessionManager.RemoveConnection(connectedAccount.Connection);
                
                await _cache.PublishAsync($"world:accounts:require_disconnect", account.Id!.Value.ToString());
            }
            
            await session.SendAsync(SAuthResultPacket.Create(null, null, AuthResult.ALREADY_CONNECTED, session.Encrypt));
            return;
        }
        
        if (!_sessionManager.PatchSession(source, account.Id!.Value))
        {
            //TODO: Fix this EXCEPTION properly
            throw new Exception("Failed to patch session");
        }
        
        // account.Online = true;
        account.LastIp = source.RemoteAddress.Split(':')[0];
        account.LastLogin = DateTime.UtcNow;
        account.FailedLogins = 0;
        
        await _databaseManager.Auth.Account.SaveAsync(account);

        await _cache.PublishAsync($"auth:accounts:online", account.Id!.Value.ToString());
        
        await session.SendAsync(SAuthResultPacket.Create(account.Id, null, AuthResult.SUCCESS, session.Encrypt));
    }

    public async Task HandleLogoutPacket(IRemoteSource source, CLogoutPacket packet)
    {
        var session = _sessionManager.GetSession(source);
        
        if (session == null)
        {
            _logger.LogWarning("Session not found for account {AccountId}", packet.AccountId);
            return;
        }

        var sessionLock = _sessionManager.GetSessionLock(session);
        
        await sessionLock.WaitAsync();
        
        await session.SendAsync(SLogoutPacket.Create(LogoutResult.Success, session.Encrypt));
        
        _sessionManager.RemoveConnection(session.Connection);

        sessionLock.Release();
    }

    public async Task HandleRegisterPacket(IRemoteSource source, CRegisterPacket packet)
    {
        var session = _sessionManager.GetSession(source);
        
        if (session == null)
        {
            _logger.LogWarning("Session not found for client {EndPoint}", source.RemoteAddress);
            return;
        }
        
        if (string.IsNullOrWhiteSpace(packet.Username))
        {
            await session.SendAsync(SRegisterResultPacket.Create(RegisterResult.EmptyUsername, session.Encrypt));
            return;
        }
        
        if (string.IsNullOrWhiteSpace(packet.Password))
        {
            await session.SendAsync(SRegisterResultPacket.Create(RegisterResult.EmptyPassword, session.Encrypt));
            return;
        }
        
        switch (packet.Password.Length)
        {
            case < 3:
                await session.SendAsync(SRegisterResultPacket.Create(RegisterResult.PasswordTooShort, session.Encrypt));
                return;
            case > 16:
                await session.SendAsync(SRegisterResultPacket.Create(RegisterResult.PasswordTooLong, session.Encrypt));
                return;
        }
        
        var salt = BCrypt.Net.BCrypt.GenerateSalt();
        var hash = BCrypt.Net.BCrypt.HashPassword(packet.Password.Trim(), salt);

        var saltBytes = Encoding.UTF8.GetBytes(salt);
        var hashBytes = Encoding.UTF8.GetBytes(hash);

        var account = new Account
        {
            Username = packet.Username.Trim(),
            Email = "Email",
            FailedLogins = 0,
            JoinDate = DateTime.UtcNow,
            LastIp = session.Connection!.RemoteAddress.Split(':')[0],
            Salt = saltBytes,
            Verifier = hashBytes,
            Online = false,
            Locale = "en",
            Locked = false,
            AccessLevel = AccountAccessLevel.Player,
            OS = "Windows",
            LastLogin = DateTime.UnixEpoch,
            MuteBy = "",
            MuteReason = "",
            MuteTime = null,
            TotalTime = 0,
            LastAttemptIp = ""
        };

        try
        {
            await _databaseManager.Auth.Account.SaveAsync(account);
        
            await session.SendAsync(SRegisterResultPacket.Create(RegisterResult.Ok, session.Encrypt));
        
            _logger.LogInformation("Account {Username} registered", packet.Username);
        }
        catch (Exception e)
        {
            await session.SendAsync(SRegisterResultPacket.Create(RegisterResult.UnknownError, session.Encrypt));
            _logger.LogError(e, "Failed to save account");
        }
    }

    public async Task HandleWorldListPacket(IRemoteSource source, CWorldListPacket packet)
    {
        var worlds = await _databaseManager.Auth.World.FindAllAsync();
        
        var session = _sessionManager.GetSession(source);
        if (session == null)
        {
            _logger.LogWarning("Session not found for client {EndPoint}", source.RemoteAddress);
            return;
        }
        
        var account = await _databaseManager.Auth.Account.FindByIdAsync(session.AccountId);
        if (account == null)
        {
            _logger.LogWarning("Account not found for session {Session}", session.AccountId);
            return;
        }
        
        worlds = worlds.Where(w => w.AccessLevelRequired <= account.AccessLevel).ToList();
        
        var worldsInfo = worlds.Select(w => new WorldInfo
        {
            Id = w.Id!.Value,
            Name = w.Name,
            Type = (short) w.Type,
            AccessLevelRequired = (short) w.AccessLevelRequired,
            Host = w.Host,
            Port = w.Port,
            MinVersion = w.MinVersion,
            Version = w.Version,
            Status = (short) w.Status,
        }).ToArray();

        await session.SendAsync(SWorldListPacket.Create(worldsInfo, session.Encrypt));
    }

    public async Task HandleWorldSelectPacket(IRemoteSource source, CWorldSelectPacket packet)
    {
        var session = _sessionManager.GetSession(source);
        if (session == null)
        {
            _logger.LogWarning("Session not found for client {EndPoint}", source.RemoteAddress);
            return;
        }
        
        var account = await _databaseManager.Auth.Account.FindByIdAsync(session.AccountId);
        if (account == null)
        {
            _logger.LogWarning("Account not found for session {Session}", session.AccountId);
            return;
        }
        
        var world = await _databaseManager.Auth.World.FindByIdAsync(packet.WorldId);
        if (world == null)
        {
            _logger.LogWarning("World not found for id {WorldId}", packet.WorldId);
            return;
        }
        
        if (world.AccessLevelRequired > account.AccessLevel)
        {
            _logger.LogWarning("Account {AccountId} tried to access world {WorldId} without the required access level", account.Id, world.Id);
            return;
        }
        
        //TODO: Check if account already in a world
        //TODO: Properly generate world key
        // generate random data
        var worldKey = new byte[32];
        new Random().NextBytes(worldKey);
        
        account.SessionKey = worldKey;
        await _databaseManager.Auth.Account.SaveAsync(account);
        
        var worldKeyBase64 = Convert.ToBase64String(worldKey);
        
        await _cache.SetAsync($"world:{world.Id}:account:{account.Id}:worldKey", worldKeyBase64, TimeSpan.FromMinutes(5));
        await _cache.PublishAsync($"world:{world.Id}:select", $"account:{account.Id}:worldKey:{worldKeyBase64}");

        await session.SendAsync(SWorldSelectPacket.Create(worldKey, session.Encrypt));
    }
}
