using System.Security.Cryptography;
using System.Text;
using Avalon.Network;
using Avalon.Network.Packets.Auth;
using BCrypt.Net;
using Microsoft.Extensions.Logging;

namespace Avalon.Game;

public partial class AvalonGame
{
    public async Task HandleAuthPacket(IRemoteSource source, CAuthPacket packet)
    {
        var client = (TcpClient) source;
        
        _logger.LogDebug("Handling auth packet from {EndPoint}", client.Socket.RemoteEndPoint);
        
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

        // $2a$11$sd9udnt8wyS/2g/xWBrjau
        // $2a$11$sd9udnt8wyS/2g/xWBrjauC4uQo7VmjsWOKrIXYw7mG9NLQ46CY96
        
        var verifier = Encoding.UTF8.GetString(account.Verifier);
        
        var hash = BCrypt.Net.BCrypt.HashPassword(packet.Password.Trim());

        if (!BCrypt.Net.BCrypt.Verify(packet.Password.Trim(), hash))
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
}
