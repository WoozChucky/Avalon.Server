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
    public async Task HandleExchangeWorldKeyPacket(IRemoteSource source, CExchangeWorldKeyPacket packet)
    {
        var worldKeyBase64 = Convert.ToBase64String(packet.WorldKey!);
        
        var id = await _cache.GetAsync($"world:{_gameConfiguration.WorldId}:keys:{worldKeyBase64}");
        if (id == null)
        {
            _logger.LogWarning("Client {EndPoint} sent an invalid world key", source.RemoteAddress);
            return;
        }
        
        await _cache.RemoveAsync($"world:{_gameConfiguration.WorldId}:keys:{worldKeyBase64}");
        
        if (!int.TryParse(id, out var accountId))
        {
            _logger.LogWarning("Client {EndPoint} sent an invalid world key", source.RemoteAddress);
            return;
        }

        var account = await _databaseManager.Auth.Account.FindByIdAsync(accountId);
        if (account == null)
        {
            _logger.LogWarning("Client {EndPoint} sent an invalid world key", source.RemoteAddress);
            return;
        }
        
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
        
        session.AccountId = accountId;
        
        var result = SExchangeWorldKeyPacket.Create(
            _cryptography.GetPublicKey()
        );
        
        await source.SendAsync(result);
    }

    public async Task HandleWorldHandshakePacket(IRemoteSource source, CWorldHandshakePacket packet)
    {
        var session = _sessionManager.GetSession(source);
        if (session == null)
        {
            _logger.LogWarning("Session not found for client {EndPoint}", source.RemoteAddress);
            return;
        }

        session.VerifyHandshakeData(session.GenerateHandshakeData());
        
        //TODO: Check if client version is allowed within this world build
        
        var result = SWorldHandshakePacket.Create(
            session.AccountId, // TODO: Get version from configuration
            true,
            session.Encrypt
        );

        if (!_sessionManager.PatchSession(source, session.AccountId))
        {
            _logger.LogWarning("Failed to patch session for client {EndPoint}", source.RemoteAddress);
            return;
        }
        
        await session.SendAsync(result);
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
}
