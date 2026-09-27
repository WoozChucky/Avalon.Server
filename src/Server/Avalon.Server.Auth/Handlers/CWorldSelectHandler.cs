using Avalon.Common.Accounts;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Hosting.Networking;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;
using Avalon.Server.Auth.Configuration;
using Microsoft.Extensions.Options;

namespace Avalon.Server.Auth.Handlers;

public class CWorldSelectHandler : IAuthPacketHandler<CWorldSelectPacket>
{
    private readonly ILogger<CWorldSelectHandler> _logger;
    private readonly IReplicatedCache _cache;
    private readonly IAccountRepository _accountRepository;
    private readonly IWorldRepository _worldRepository;
    private readonly ISecureRandom _secureRandom;
    private readonly TimeProvider _time;
    private readonly int _maxSelectsPerWindow;

    public CWorldSelectHandler(ILoggerFactory loggerFactory, IReplicatedCache cache, IAccountRepository accountRepository,
        IWorldRepository worldRepository, ISecureRandom secureRandom, IOptions<AuthConfiguration> options, TimeProvider time)
    {
        _logger = loggerFactory.CreateLogger<CWorldSelectHandler>();
        _cache = cache;
        _accountRepository = accountRepository;
        _worldRepository = worldRepository;
        _secureRandom = secureRandom;
        _time = time;
        _maxSelectsPerWindow = options.Value.MaxWorldSelectsPerMinute;
    }

    public async Task ExecuteAsync(AuthPacketContext<CWorldSelectPacket> ctx, CancellationToken token = default)
    {
        // #574: counted before anything is read, so a select past the cap costs no database read.
        if (!Admit(ctx.Connection))
        {
            ctx.Connection.Close();
            return;
        }

        // Defence in depth behind CAuthHandler (#462, #495): an account banned or deactivated, or
        // whose credentials changed, since this connection logged in takes no inWorld slot and is
        // issued no world key.
        var account = await PostLoginGuard.AccountOrCloseAsync(ctx.Connection, _accountRepository, _logger,
            "world select", token);
        if (account == null)
            return;

        // #554: an unknown world and a world this account may not enter get the same answer,
        // WorldUnavailable, after the same single lookup, so a client cannot probe for restricted
        // worlds. Neither takes the inWorld slot or writes a key, and the connection stays open so
        // the player can pick another world.
        var world = await _worldRepository.FindByIdAsync(ctx.Packet.WorldId, false, token);
        if (world == null)
        {
            _logger.Log(UnavailableLevel(ctx.Connection), "World not found for id {WorldId}", ctx.Packet.WorldId);
            SendWorldUnavailable(ctx.Connection);
            return;
        }

        // The same rule as the world list, so a world never listed can never be selected either.
        if (!AccessLevels.ForWorld(world.AccessLevelRequired).Allows(account.AccessLevel))
        {
            _logger.Log(UnavailableLevel(ctx.Connection),
                "Account {AccountId} tried to access world {WorldId} without the required access level", account.Id, world.Id);
            SendWorldUnavailable(ctx.Connection);
            return;
        }

        bool sessionSlotAcquired = await _cache.SetNxAsync(CacheKeys.AccountInWorld(account.Id), "1", TimeSpan.FromMinutes(5));
        if (!sessionSlotAcquired)
        {
            _logger.LogWarning("Account {AccountId} attempted to enter world {WorldId} while already holding an active session", account.Id, world.Id);
            ctx.Connection.Send(SWorldSelectPacket.CreateError(WorldSelectResult.DuplicateSession, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        var worldKey = _secureRandom.GetBytes(32);

        // Only the key (#484): writing back the row as read would undo a lock or a ban written since.
        account.SessionKey = worldKey;
        await _accountRepository.SetSessionKeyAsync(account.Id, worldKey, token);

        var worldKeyBase64 = Convert.ToBase64String(worldKey);

        // With the version this connection's login proved (#495): the exchange refuses the key once
        // the account has moved past it.
        await _cache.SetAsync(CacheKeys.WorldKey(world.Id.Value, worldKeyBase64),
            CacheKeys.WorldKeyValue(account.Id.Value, ctx.Connection.CredentialsVersion), TimeSpan.FromMinutes(5));
        await _cache.PublishAsync(CacheKeys.WorldSelectChannel(world.Id.Value), $"account:{account.Id}:worldKey:{worldKeyBase64}");

        ctx.Connection.Send(SWorldSelectPacket.Create(worldKey, ctx.Connection.CryptoSession.Encrypt));
    }

    /// <summary>
    /// Takes one select from the connection's budget (#574); false past the cap, and the caller
    /// closes the connection. The refusal is logged once per window, since selects already on
    /// their way keep arriving until the connection is gone.
    /// </summary>
    private bool Admit(IAuthConnection connection)
    {
        WorldSelectAdmission admission = connection.WorldSelects.Take(_time, _maxSelectsPerWindow);
        if (admission == WorldSelectAdmission.Admitted)
            return true;

        if (admission == WorldSelectAdmission.RefusedFirstInWindow)
        {
            _logger.LogWarning(
                "Connection {Session} from {Endpoint} exceeded the world select budget of {Max} per minute; closing it",
                connection.Id, connection.RemoteEndPoint, _maxSelectsPerWindow);
        }

        return false;
    }

    /// <summary>A WorldUnavailable refusal is logged at Warning once per connection per window, at Debug after that (#574).</summary>
    private static LogLevel UnavailableLevel(IAuthConnection connection)
        => connection.WorldSelects.TakeUnavailableWarning() ? LogLevel.Warning : LogLevel.Debug;

    private static void SendWorldUnavailable(IAuthConnection connection)
        => connection.Send(SWorldSelectPacket.CreateError(WorldSelectResult.WorldUnavailable, connection.CryptoSession.Encrypt));
}
