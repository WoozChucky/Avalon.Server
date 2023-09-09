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
        var client = (TcpClient) source;
        
        LoggerExtensions.LogDebug(_logger, "Handling auth packet from {EndPoint}", client.Socket.RemoteEndPoint);
        
        if (string.IsNullOrWhiteSpace(packet.Username) || string.IsNullOrWhiteSpace(packet.Password))
        {
            await _packetSerializer.SerializeToNetwork(client.Stream, SAuthResultPacket.Create(AuthResult.INVALID_CREDENTIALS));
            return;
        }

        var account =
            await _databaseManager.Auth.Account.QueryByUsernameAsync(packet.Username.ToUpperInvariant().Trim());

        if (account == null)
        {
            await _packetSerializer.SerializeToNetwork(client.Stream, SAuthResultPacket.Create(AuthResult.INVALID_CREDENTIALS));
            return;
        }
        
        var verifier = Encoding.UTF8.GetString((byte[])account.Verifier);

        if (!BCrypt.Net.BCrypt.Verify(packet.Password.Trim(), verifier))
        {
            //TODO: Increment failed login attempts
            
            await _packetSerializer.SerializeToNetwork(client.Stream, SAuthResultPacket.Create(AuthResult.INVALID_CREDENTIALS));
            return;
        }
        
        // TODO: Check if account is locked
            
        //var salt = BCrypt.Net.BCrypt.GenerateSalt();
        //var hash = BCrypt.Net.BCrypt.HashPassword("123", salt);

        // Generate 256 bits private key for this client
        var privateKey = new byte[32]; // 256 bits = 32 bytes
        using (var rng = new RNGCryptoServiceProvider())
        {
            rng.GetBytes(privateKey);
        }

        _connectionManager.AddSession(source, account.Id, privateKey);
        
        await _packetSerializer.SerializeToNetwork(client.Stream, SAuthResultPacket.Create(account.Id, privateKey));
    }

    public async Task HandleAuthPatchPacket(IRemoteSource source, CAuthPatchPacket packet)
    {
        var udpClient = source.AsUdpClient();
        
        if (!_connectionManager.PatchSession(source, packet.AccountId, packet.PrivateKey))
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
            LoggerExtensions.LogWarning(_logger, "Session not found for account {AccountId}", packet.AccountId);
            return;
        }
        
        if (!session.InGame)
        {
            LoggerExtensions.LogWarning(_logger, "Session {AccountId} is not in game", packet.AccountId);
            await session.SendAsync(SLogoutPacket.Create(session.AccountId, LogoutResult.NotInGame));
            return;
        }
        
        // Save character progress to the database
        var character = session.Character;
        
        // TODO: Calculate play time
        
        character!.Online = false;

        if (!await _databaseManager.Characters.Character.UpdateAsync(character))
        {
            LoggerExtensions.LogWarning(_logger, "Failed to save character {CharacterId} progress to the database", character.Name);
            await session.SendAsync(SLogoutPacket.Create(session.AccountId, LogoutResult.InternalError));
        }
        
        LoggerExtensions.LogInformation(_logger, "Character {CharacterId} logged out at {Position}", character.Name, character.Movement);
        
        await BroadcastToOthersInInstance(session.AccountId, SPlayerDisconnectedPacket.Create(session.AccountId, session.Character!.Id), session.Character.InstanceId);
        
        session.Character = null;
        
        await session.SendAsync(SLogoutPacket.Create(session.AccountId, LogoutResult.Success));
    }
}
