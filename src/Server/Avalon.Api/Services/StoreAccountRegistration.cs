using System.Net;
using Avalon.Api.Config;
using Avalon.Common.GameAuth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.GameAuth;
using Avalon.Infrastructure.Login;

namespace Avalon.Api.Services;

/// <summary>Creates a store-backed Avalon root and identity in one transaction, after provider verification.</summary>
public sealed class StoreAccountRegistration(IExternalIdentityRepository identities, IReplicatedCache cache,
    AuthenticationConfig config, TimeProvider clock) : IGameAccountRegistration
{
    public Task<IdentityLinkResult> CreateFromSteamAsync(Guid id, string subject, DateTime end, string address, CancellationToken ct) =>
        CreateFromStoreAsync(id, StoreProviders.Steam, subject, end, address, ct);

    public async Task<IdentityLinkResult> CreateFromStoreAsync(Guid operationId, string provider, string verifiedSubject,
        DateTime proofExpiresAt, string sourceAddress, CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty || !IPAddress.TryParse(sourceAddress, out IPAddress? source) ||
            proofExpiresAt <= clock.GetUtcNow().UtcDateTime)
            return new(IdentityLinkStatus.AuthorityChanged, null);
        var account = new Account
        {
            // Internal account name, independent of an untrusted store display name. Stable across exact retries.
            Username = "S" + operationId.ToString("N")[..15].ToUpperInvariant(),
            Email = null,
            Salt = [],
            Verifier = [],
            IsStoreGenerated = true,
            JoinDate = clock.GetUtcNow().UtcDateTime,
            LastIp = source.ToString(),
        };
        var operation = new StoreAccountCreationOperation(operationId, account, verifiedSubject, proofExpiresAt) { Provider = provider };
        ExternalIdentity? prior = await identities.FindAsync(provider, verifiedSubject, cancellationToken);
        if (prior?.Id == operationId)
            return await identities.CreateAccountWithStoreLinkAsync(operation, clock.GetUtcNow().UtcDateTime, cancellationToken);
        string key = CacheKeys.AuthSourceAccountsCreated(RemoteAddress.SourceOf(source));
        long taken = await AttemptBudget.TakeAsync(cache, key, TimeSpan.FromMinutes(config.AccountCreationWindowMinutes));
        if (taken > config.MaxAccountsCreatedPerSource)
        {
            await AttemptBudget.GiveBackAsync(cache, key);
            return new(IdentityLinkStatus.CreationRefused, null);
        }
        // An uncertain DB outcome keeps its reservation. The durable operation receipt recovers retries.
        IdentityLinkResult result = await identities.CreateAccountWithStoreLinkAsync(operation, clock.GetUtcNow().UtcDateTime, cancellationToken);
        if (result.Status != IdentityLinkStatus.Linked) await AttemptBudget.GiveBackAsync(cache, key);
        return result;
    }
}
