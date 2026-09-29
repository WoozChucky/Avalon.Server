using Avalon.Common.Accounts;
using Avalon.Common.Telemetry;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.GameTickets;
using Avalon.Infrastructure.Login;
using Avalon.Network.Packets.Auth;
using Avalon.Hosting.Telemetry;
using Avalon.Server.Auth.Telemetry;

namespace Avalon.Server.Auth.Handlers;

public class CAuthGameTicketHandler : IAuthPacketHandler<CAuthGameTicketPacket>
{
    private readonly ILogger<CAuthGameTicketHandler> _logger;
    private readonly IGameTicketStore _tickets;
    private readonly IRefreshTokenRepository _families;
    private readonly IAccountRepository _accounts;
    private readonly IReplicatedCache _cache;

    public CAuthGameTicketHandler(ILoggerFactory loggerFactory, IGameTicketStore tickets,
        IRefreshTokenRepository families, IAccountRepository accounts, IReplicatedCache cache)
    {
        _logger = loggerFactory.CreateLogger<CAuthGameTicketHandler>();
        _tickets = tickets;
        _families = families;
        _accounts = accounts;
        _cache = cache;
    }

    public async Task ExecuteAsync(AuthPacketContext<CAuthGameTicketPacket> ctx, CancellationToken token = default)
    {
        var connection = ctx.Connection;
        GameTicketGrant? grant;
        try
        {
            // Consume before account checks so every attempt stays single-use, including
            // ALREADY_CONNECTED when a stale online session is being cleared.
            grant = await _tickets.RedeemAsync(ctx.Packet.Ticket, token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Game ticket redemption unavailable");
            Refuse(AuthResult.INVALID_CREDENTIALS);
            return;
        }

        if (grant is null)
        {
            Refuse(AuthResult.INVALID_CREDENTIALS);
            return;
        }

        bool familyLive;
        try
        {
            familyLive = await _families.IsLiveLauncherFamilyAsync(grant.AccountId, grant.FamilyId,
                DateTime.UtcNow, token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Launcher session check unavailable during game login");
            Refuse(AuthResult.INVALID_CREDENTIALS);
            return;
        }

        if (!familyLive)
        {
            Refuse(AuthResult.INVALID_CREDENTIALS);
            return;
        }

        Account? account = await _accounts.FindByIdAsync(grant.AccountId, false, token);
        if (account is null || account.CredentialsVersion != grant.CredentialsVersion ||
            !AccessLevels.Player.Allows(account.AccessLevel))
        {
            Refuse(AuthResult.INVALID_CREDENTIALS);
            return;
        }

        if (account.Status != AccountStatus.Active)
        {
            Refuse(account.Status == AccountStatus.Deactivated ? AuthResult.DEACTIVATED : AuthResult.BANNED);
            return;
        }

        if (account.IsLockedAt(DateTime.UtcNow))
        {
            Refuse(AuthResult.LOCKED);
            return;
        }

        string lastIp = LoginSource.FromEndPoint(connection.RemoteEndPoint).Ip;
        AuthResult? startRefusal = await GameLoginCompletion.TryStartAsync(connection, account, lastIp,
            AuthResult.LOCKED, _accounts, _cache, _logger, token, ticket: true);
        if (startRefusal is { } result)
        {
            Record(result, account);
            return;
        }

        await GameLoginCompletion.FinishAsync(connection, account, lastIp, _cache, ticket: true);
        Record(AuthResult.SUCCESS, account);

        void Refuse(AuthResult result)
        {
            connection.Send(SAuthResultPacket.Create(null, null, result, connection.CryptoSession.Encrypt));
            Record(result, null);
        }

        void Record(AuthResult result, Account? known)
        {
            string tag = LoginTelemetry.Tag(result);
            DiagnosticsConfig.Auth.Logins.Add(1, new KeyValuePair<string, object?>("result", tag));
            _logger.Log(LoginTelemetry.LogLevelFor(tag),
                "Game ticket login {LoginResult} for account {AccountId} from {ClientAddress}",
                tag, known?.Id.Value, PacketTags.AddressOf(connection.RemoteEndPoint));
        }
    }
}
