using System.Security.Cryptography;
using System.Text;
using Avalon.Network;
using Avalon.Network.Packets.Auth;
using Microsoft.Extensions.Logging;

namespace Avalon.Game;

public partial class AvalonGame
{
    public async Task HandleAuthPacket(IRemoteSource source, CAuthPacket packet)
    {
        var client = source.AsTcpClient();
        
        _logger.LogDebug("Handling auth packet from {EndPoint}", client.Socket.RemoteEndPoint);
        
        var session = _connectionManager.GetSession(client);

        if (session == null)
        {
            _logger.LogWarning("Session not found for client {EndPoint}", client.Socket.RemoteEndPoint);
            return;
        }
        
        if (string.IsNullOrWhiteSpace(packet.Username) || string.IsNullOrWhiteSpace(packet.Password))
        {
            await client.SendAsync(SAuthResultPacket.Create(AuthResult.INVALID_CREDENTIALS, session.Encrypt));
            return;
        }

        var account =
            await _databaseManager.Auth.Account.QueryByUsernameAsync(packet.Username.ToUpperInvariant().Trim());

        if (account == null)
        {
            await client.SendAsync(SAuthResultPacket.Create(AuthResult.INVALID_CREDENTIALS, session.Encrypt));
            return;
        }
        
        var verifier = Encoding.UTF8.GetString(account.Verifier);

        if (!BCrypt.Net.BCrypt.Verify(packet.Password.Trim(), verifier))
        {
            //TODO: Increment failed login attempts
            
            await client.SendAsync(SAuthResultPacket.Create(AuthResult.INVALID_CREDENTIALS, session.Encrypt));
            return;
        }
        
        // TODO: Check if account is locked

        await client.SendAsync(SAuthResultPacket.Create(account.Id, AuthResult.PENDING_KEY, session.Encrypt));
    }

    public async Task HandleAuthPatchPacket(IRemoteSource source, CAuthPatchPacket packet)
    {
        var udpClient = source.AsUdpClient();
        
        if (!_connectionManager.PatchSession(source, packet.AccountId, packet.PublicKey))
        {
            
            await udpClient.SendAsync(SAuthResultPacket.Create(AuthResult.WRONG_KEY));
        }
        
        await udpClient.SendAsync(SAuthResultPacket.Create(packet.AccountId, AuthResult.SUCCESS));
    }
    
    public async Task HandleLogoutPacket(IRemoteSource source, CLogoutPacket packet)
    {
        var session = _connectionManager.GetSession(packet.AccountId);
        
        if (session == null)
        {
            _logger.LogWarning("Session not found for account {AccountId}", packet.AccountId);
            return;
        }
        
        if (!session.InGame)
        {
            _logger.LogWarning("Session {AccountId} is not in game", packet.AccountId);
            await session.SendAsync(SLogoutPacket.Create(session.AccountId, LogoutResult.NotInGame));
            return;
        }
        
        // Save character progress to the database
        var character = session.Character;
        
        // TODO: Calculate play time
        
        character!.Online = false;

        if (!await _databaseManager.Characters.Character.UpdateAsync(character))
        {
            _logger.LogWarning("Failed to save character {CharacterId} progress to the database", character.Name);
            await session.SendAsync(SLogoutPacket.Create(session.AccountId, LogoutResult.InternalError));
        }
        
        _logger.LogInformation("Character {CharacterId} logged out at {Position}", character.Name, character.Movement);
        
        await BroadcastToOthersInInstance(session.AccountId, SPlayerDisconnectedPacket.Create(session.AccountId, session.Character!.Id), session.Character.InstanceId);
        
        session.Character = null;
        
        await session.SendAsync(SLogoutPacket.Create(session.AccountId, LogoutResult.Success));
    }
}
