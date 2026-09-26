using Avalon.Common.Accounts;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Hosting.Networking;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;

namespace Avalon.Server.Auth.Handlers;

public class CWorldSelectHandler : IAuthPacketHandler<CWorldSelectPacket>
{
    private readonly ILogger<CHandshakeHandler> _logger;
    private readonly IReplicatedCache _cache;
    private readonly IAccountRepository _accountRepository;
    private readonly IWorldRepository _worldRepository;
    private readonly ISecureRandom _secureRandom;

    public CWorldSelectHandler(ILoggerFactory loggerFactory, IReplicatedCache cache, IAccountRepository accountRepository, IWorldRepository worldRepository, ISecureRandom secureRandom)
    {
        _logger = loggerFactory.CreateLogger<CHandshakeHandler>();
        _cache = cache;
        _accountRepository = accountRepository;
        _worldRepository = worldRepository;
        _secureRandom = secureRandom;
    }

    public async Task ExecuteAsync(AuthPacketContext<CWorldSelectPacket> ctx, CancellationToken token = default)
    {

        var account = await _accountRepository.FindByIdAsync(ctx.Connection.AccountId ?? 0, false, token);
        if (account == null)
        {
            _logger.LogWarning("Account not found for connection {Session}", ctx.Connection.Id);
            ctx.Connection.Close();
            return;
        }

        // Defence in depth behind CAuthHandler (#462): an account banned or deactivated after it
        // logged in must not take the inWorld slot or be issued a world key.
        if (account.Status != AccountStatus.Active)
        {
            _logger.LogWarning("Account {AccountId} tried to select a world while {Status}", account.Id, account.Status);
            ctx.Connection.Close();
            return;
        }

        // The connection proved the credentials at its login (#495). A password change, an MFA
        // reset or an admin's MFA removal since then ends it: no world key for the old credentials.
        if (account.CredentialsVersion != ctx.Connection.CredentialsVersion)
        {
            _logger.LogWarning("Account {AccountId} tried to select a world after its credentials changed", account.Id);
            ctx.Connection.Close();
            return;
        }

        var world = await _worldRepository.FindByIdAsync(ctx.Packet.WorldId, false, token);
        if (world == null)
        {
            _logger.LogWarning("World not found for id {WorldId}", ctx.Packet.WorldId);
            return;
        }

        // The same rule as the world list, so a world never listed can never be selected either.
        if (!AccessLevels.ForWorld(world.AccessLevelRequired).Allows(account.AccessLevel))
        {
            _logger.LogWarning("Account {AccountId} tried to access world {WorldId} without the required access level", account.Id, world.Id);
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
}
