using System.Text;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Network;
using Avalon.Network.Packets.Auth;
using Microsoft.Extensions.Logging;
using OtpNet;

namespace Avalon.Game;

public partial class AvalonGame
{
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
        
        var verifier = Encoding.UTF8.GetString(account.Verifier);

        if (!BCrypt.Net.BCrypt.Verify(packet.Password.Trim(), verifier))
        {
            //TODO: Increment failed login attempts
            
            await session.SendAsync(SAuthResultPacket.Create(null, null, AuthResult.INVALID_CREDENTIALS, session.Encrypt));
            return;
        }
        
        // TODO: Check if account is locked
        
        if (!_sessionManager.PatchSession(source, account.Id!.Value))
        {
            //TODO: Fix this EXCEPTION properly
            throw new Exception("Failed to patch session");
        }
        
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

        try
        {
            if (!session.InGame)
            {
                _logger.LogWarning("Session {AccountId} is not in game", packet.AccountId);
                await session.SendAsync(SLogoutPacket.Create(LogoutResult.NotInGame, session.Encrypt));
                return;
            }

            if (session.AccountId != packet.AccountId)
            {
                _logger.LogWarning("Session {AccountId} is not the same as the packet {PacketAccountId}", session.AccountId, packet.AccountId);
                await session.SendAsync(SLogoutPacket.Create(LogoutResult.NotSameAccount, session.Encrypt));
            }

            if (session.InMap)
            {
                if (!_mapManager.RemoveSessionFromMap(session))
                {
                    _logger.LogWarning("Failed to remove session {AccountId} from map", session.AccountId);
                }
            }
    
            // Save character progress to the database
            var character = session.Character;
    
            // TODO: Calculate play time
    
            character!.Online = false;

            try
            {
                await _databaseManager.Characters.Character.UpdateAsync(character);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to save character {CharacterId} progress to the database", character.Name);
                await session.SendAsync(SLogoutPacket.Create(LogoutResult.InternalError, session.Encrypt));
            }
    
            _logger.LogInformation("Character {CharacterId} logged out at {Position}", character.Name, character.Movement);
            
            var availableSessions = _sessionManager.GetSessions().Values.Where(
                s => 
                    s.AccountId != session.AccountId 
                    && s is { Status: ConnectionStatus.Connected, Character: not null } 
                    && s.Character.InstanceId == session.Character!.InstanceId
            );

            var tasks = availableSessions.Select(s => s.SendAsync(SPlayerDisconnectedPacket.Create(session.AccountId, session.Character!.Id!.Value, s.Encrypt)));
            
            await Task.WhenAll(tasks);
            
            session.Character = null;
    
            await session.SendAsync(SLogoutPacket.Create(LogoutResult.Success, session.Encrypt));
        }
        finally
        {
            sessionLock.Release();
        }
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
            return;
        }
    }
}
