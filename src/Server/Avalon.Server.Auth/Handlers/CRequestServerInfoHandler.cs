using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Handshake;

namespace Avalon.Server.Auth.Handlers;

public class CRequestServerInfoHandler : IAuthPacketHandler<CRequestServerInfoPacket>
{
    private readonly ILogger<CRequestServerInfoHandler> _logger;

    public CRequestServerInfoHandler(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<CRequestServerInfoHandler>();
    }

    public Task ExecuteAsync(AuthPacketContext<CRequestServerInfoPacket> ctx, CancellationToken token = default)
    {
        if (ctx.Packet.ClientVersion != "0.0.1") // TODO: Hardcoded client version
        {
            _logger.LogWarning("Client {EndPoint} is using an invalid version", ctx.Connection.Id);
            ctx.Connection.Close();
            return Task.CompletedTask;
        }

        var result = SServerInfoPacket.Create(
            1_000_000, // TODO: Hardcoded server version
            ctx.Connection.ServerCrypto.GetPublicKey()
        );

        ctx.Connection.Send(result);

        return Task.CompletedTask;
    }
}
