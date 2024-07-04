using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Handshake;

namespace Avalon.Server.Auth.Handlers;

public class CClientInfoHandler : IAuthPacketHandler<CClientInfoPacket>
{
    private readonly ILogger<CClientInfoHandler> _logger;
    
    public CClientInfoHandler(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<CClientInfoHandler>();
    }
    
    public Task ExecuteAsync(AuthPacketContext<CClientInfoPacket> ctx, CancellationToken token = default)
    {
        var packet = ctx.Packet;
        
        if (packet.PublicKey == null || packet.PublicKey.Length == 0)
        {
            _logger.LogWarning("Client {EndPoint} sent an invalid public key", ctx.Connection.Id);
            return Task.CompletedTask;
        }

        if (packet.PublicKey.Length != ctx.Connection.ServerCrypto.GetValidKeySize())
        {
            _logger.LogWarning("Client {EndPoint} sent an invalid public key size", ctx.Connection.Id);
            return Task.CompletedTask;
        }
        
        ctx.Connection.CryptoSession.Initialize(packet.PublicKey);

        var data = ctx.Connection.GenerateHandshakeData();
        
        var result = SHandshakePacket.Create(data, ctx.Connection.CryptoSession.Encrypt);
        
        ctx.Connection.Send(result);
        
        return Task.CompletedTask;
    }
}
