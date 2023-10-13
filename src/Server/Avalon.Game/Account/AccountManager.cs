using System.Text;
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
            await session.SendAsync(SAuthResultPacket.Create(null, AuthResult.INVALID_CREDENTIALS, session.Encrypt));
            return;
        }

        var account =
            await _databaseManager.Auth.Account.QueryByUsernameAsync(packet.Username.ToUpperInvariant().Trim());

        if (account == null)
        {
            await session.SendAsync(SAuthResultPacket.Create(null, AuthResult.INVALID_CREDENTIALS, session.Encrypt));
            return;
        }
        
        var verifier = Encoding.UTF8.GetString(account.Verifier);

        if (!BCrypt.Net.BCrypt.Verify(packet.Password.Trim(), verifier))
        {
            //TODO: Increment failed login attempts
            
            await session.SendAsync(SAuthResultPacket.Create(null, AuthResult.INVALID_CREDENTIALS, session.Encrypt));
            return;
        }
        
        // TODO: Check if account is locked
        
        if (!_sessionManager.PatchSession(source, account.Id!.Value))
        {
            //TODO: Fix this EXCEPTION properly
            throw new Exception("Failed to patch session");
        }
        
        await session.SendAsync(SAuthResultPacket.Create(account.Id, AuthResult.SUCCESS, session.Encrypt));
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
                await session.SendAsync(SLogoutPacket.Create(session.AccountId, LogoutResult.NotInGame, session.Encrypt));
                return;
            }

            if (session.AccountId != packet.AccountId)
            {
                _logger.LogWarning("Session {AccountId} is not the same as the packet {PacketAccountId}", session.AccountId, packet.AccountId);
                await session.SendAsync(SLogoutPacket.Create(session.AccountId, LogoutResult.NotSameAccount, session.Encrypt));
            }
    
            // Save character progress to the database
            var character = session.Character;
    
            // TODO: Calculate play time
    
            character!.Online = false;

            if (!await _databaseManager.Characters.Character.UpdateAsync(character))
            {
                _logger.LogWarning("Failed to save character {CharacterId} progress to the database", character.Name);
                await session.SendAsync(SLogoutPacket.Create(session.AccountId, LogoutResult.InternalError, session.Encrypt));
            }
    
            _logger.LogInformation("Character {CharacterId} logged out at {Position}", character.Name, character.Movement);
            
            var availableSessions = _sessionManager.GetSessions().Values.Where(
                s => 
                    s.AccountId != session.AccountId 
                    && s is { Status: ConnectionStatus.Connected, Character: not null } 
                    && s.Character.InstanceId == session.Character!.InstanceId
            );

            var tasks = availableSessions.Select(s => s.SendAsync(SPlayerDisconnectedPacket.Create(session.AccountId, session.Character!.Id, s.Encrypt)));
            
            await Task.WhenAll(tasks);
            
            session.Character = null;
    
            await session.SendAsync(SLogoutPacket.Create(session.AccountId, LogoutResult.Success, session.Encrypt));
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
            case > 12:
                await session.SendAsync(SRegisterResultPacket.Create(RegisterResult.PasswordTooLong, session.Encrypt));
                return;
        }
        
        var salt = BCrypt.Net.BCrypt.GenerateSalt();
        var hash = BCrypt.Net.BCrypt.HashPassword(packet.Password.Trim(), salt);

        var saltBytes = Encoding.UTF8.GetBytes(salt);
        var hashBytes = Encoding.UTF8.GetBytes(hash);
        
        var totpSecret = KeyGeneration.GenerateRandomKey(32);
        
        var inserted = await _databaseManager.Auth.Account.InsertAccountAsync(
            packet.Username.Trim(), 
            "", 
            totpSecret, 
            saltBytes, 
            hashBytes, 
            session.Connection!.RemoteAddress.Split(':')[0]
        );
        
        if (!inserted)
        {
            await session.SendAsync(SRegisterResultPacket.Create(RegisterResult.UnknownError, session.Encrypt));
            return;
        }
        
        await session.SendAsync(SRegisterResultPacket.Create(RegisterResult.Ok, session.Encrypt));
        
        _logger.LogInformation("Account {Username} registered", packet.Username);
    }
}
