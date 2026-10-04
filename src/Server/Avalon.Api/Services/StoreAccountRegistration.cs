using Avalon.Common.GameAuth;
using System.Net;
using Avalon.Api.Config;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.GameAuth;
using Avalon.Infrastructure.Login;

namespace Avalon.Api.Services;

/// <summary>Creates a Steam-only Avalon root and identity in one transaction, after provider verification.</summary>
public sealed class StoreAccountRegistration(IExternalIdentityRepository identities, IReplicatedCache cache,
    AuthenticationConfig config, TimeProvider clock) : IGameAccountRegistration
{
    public async Task<IdentityLinkResult> CreateFromSteamAsync(Guid operationId, string verifiedSteamId,
        DateTime proofExpiresAt, string sourceAddress, CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty || !IPAddress.TryParse(sourceAddress, out var source) ||
            proofExpiresAt <= clock.GetUtcNow().UtcDateTime)
            return new(IdentityLinkStatus.AuthorityChanged, null);
        var account = new Account
        {
            // Internal account name, independent of an untrusted store display name. Stable across exact retries.
            Username = "S" + operationId.ToString("N")[..15].ToUpperInvariant(),
            Email = null, Salt = [], Verifier = [], IsStoreGenerated = true,
            JoinDate = clock.GetUtcNow().UtcDateTime, LastIp = source.ToString(),
        };
        var operation = new StoreAccountCreationOperation(operationId, account, verifiedSteamId, proofExpiresAt);
        var prior = await identities.FindAsync(StoreProviders.Steam, verifiedSteamId, cancellationToken);
        if (prior?.Id == operationId)
            return await identities.CreateAccountWithSteamLinkAsync(operation, clock.GetUtcNow().UtcDateTime, cancellationToken);
        var key = CacheKeys.AuthSourceAccountsCreated(RemoteAddress.SourceOf(source));
        var taken = await AttemptBudget.TakeAsync(cache, key, TimeSpan.FromMinutes(config.AccountCreationWindowMinutes));
        if (taken > config.MaxAccountsCreatedPerSource)
        {
            await AttemptBudget.GiveBackAsync(cache, key);
            return new(IdentityLinkStatus.CreationRefused, null);
        }
        // An uncertain DB outcome keeps its reservation. The durable operation receipt recovers retries.
        var result = await identities.CreateAccountWithSteamLinkAsync(operation, clock.GetUtcNow().UtcDateTime, cancellationToken);
        if (result.Status != IdentityLinkStatus.Linked) await AttemptBudget.GiveBackAsync(cache, key);
        return result;
    }
}
