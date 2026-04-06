using Avalon.Common.Utils;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Handshake;
using Avalon.Server.Auth.Configuration;
using Microsoft.Extensions.Options;

namespace Avalon.Server.Auth.Handlers;

public class CRequestServerInfoHandler : IAuthPacketHandler<CRequestServerInfoPacket>
{
    private readonly ILogger<CRequestServerInfoHandler> _logger;
    private readonly AuthConfiguration _authConfig;

    public CRequestServerInfoHandler(ILoggerFactory loggerFactory, IOptions<AuthConfiguration> options)
    {
        _logger = loggerFactory.CreateLogger<CRequestServerInfoHandler>();
        _authConfig = options.Value;
    }

    public Task ExecuteAsync(AuthPacketContext<CRequestServerInfoPacket> ctx, CancellationToken token = default)
    {
        uint serverVersion = SemVerPacker.Pack(_authConfig.ServerVersion);

        if (!SemVerPacker.TryPack(ctx.Packet.ClientVersion, out uint clientVersion) ||
            clientVersion < SemVerPacker.Pack(_authConfig.MinClientVersion))
        {
            _logger.LogWarning("Client {EndPoint} version {ClientVersion} is below minimum required {MinClientVersion}",
                ctx.Connection.Id, ctx.Packet.ClientVersion, _authConfig.MinClientVersion);
            ctx.Connection.Send(SServerInfoPacket.CreateRejected(ServerInfoResult.ClientVersionTooOld, serverVersion));
            ctx.Connection.Close();
            return Task.CompletedTask;
        }

        var result = SServerInfoPacket.Create(serverVersion, ctx.Connection.ServerCrypto.GetPublicKey());

        ctx.Connection.Send(result);

        return Task.CompletedTask;
    }
}
