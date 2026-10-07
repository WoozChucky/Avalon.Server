using System.Runtime.CompilerServices;
using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.GameAuth;
using Avalon.Infrastructure.StoreAuth;
using Microsoft.Extensions.Options;

namespace Avalon.Api.UnitTests.GameAuth;

internal static class TestGameAuthorization
{
    private static readonly ConditionalWeakTable<IGameContextStore, MemoryGameLicenses> s_repositories = new();
    public static MemoryGameLicenses Licenses(IGameContextStore store) => s_repositories.GetValue(store, _ => new());
    public static GameAuthorizationService Create(IGameContextStore store, AuthAttemptStore attempts, GameAuthCryptography crypto,
        IAccountRepository accounts, IRefreshTokenRepository families, IExternalIdentityRepository identities,
        ILicenseObservationRepository observations, ISteamProofVerifier proof, ISteamOwnershipClient ownership,
        IOptions<StoreAuthenticationConfiguration> options, TimeProvider clock, IGameAccountRegistration? registration = null,
        IGameContextRevocations? revocations = null, MemoryGameLicenses? gameLicenses = null)
    {
        var registry = new GameProviderRegistry([new SteamIdentityProvider(proof, options, clock)],
            [new SteamLicenseProvider(ownership), new AvalonLicenseProvider(gameLicenses ?? Licenses(store))]);
        return new(store, attempts, crypto, accounts, families, identities, registry,
            new GameLicenseAuthorityService(registry, gameLicenses ?? Licenses(store), observations, options, clock), options, clock, registration, revocations);
    }
}

internal sealed class MemoryGameLicenses : IGameLicenseRepository
{
    public List<GameLicense> Rows { get; } = [];
    public Task<GameLicense?> FindAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Rows.SingleOrDefault(x => x.Id == id));
    public Task<GameLicense?> FindAsync(AccountId account, string provider, string environment, string reference, CancellationToken ct = default) =>
        Task.FromResult(Rows.SingleOrDefault(x => x.AccountId == account && x.Provider == provider && x.Environment == environment && x.LicenseReference == reference));
    public Task<GameLicense?> FindActiveAsync(AccountId account, string provider, string environment, string product, string providerProductId, DateTime now, CancellationToken ct = default) =>
        Task.FromResult(Rows.OrderBy(x => x.GrantedAt).ThenBy(x => x.Id).FirstOrDefault(x => x.Provider == provider && x.ProviderProductId == providerProductId && x.Authorizes(account, product, environment, now)));
    public Task<GameLicense> RecordGrantAsync(GameLicense row, CancellationToken ct = default)
    {
        GameLicense? existing = Rows.SingleOrDefault(x => x.Provider == row.Provider && x.Environment == row.Environment && x.LicenseReference == row.LicenseReference);
        if (existing is not null)
        {
            if (existing.AccountId != row.AccountId || existing.Product != row.Product || existing.ProviderSubject != row.ProviderSubject || existing.ProviderProductId != row.ProviderProductId)
                throw new InvalidOperationException("Conflicting source");
            return Task.FromResult(existing);
        }
        Rows.Add(row); return Task.FromResult(row);
    }
    public Task<GameLicense?> ApplyDecisionAsync(Guid id, long expectedRevision, LicenseAuthorityDecision decision, CancellationToken ct = default)
    {
        GameLicense? row = Rows.SingleOrDefault(x => x.Id == id);
        if (row is null || row.AuthorityRevision != expectedRevision || decision.ObservedAt < row.LastObservedAt) return Task.FromResult<GameLicense?>(null);
        if (row.RevokedAt is not null && decision.OwnsProduct)
        {
            if (row.AuthorityKind == LicenseAuthorityKind.StoredGrant || !decision.Reestablish || decision.ObservedAt <= row.LastObservedAt) return Task.FromResult<GameLicense?>(null);
            row.RevokedAt = null; row.AuthorityRevision++;
        }
        else if (!decision.OwnsProduct && row.RevokedAt is null) { row.RevokedAt = decision.ObservedAt; row.AuthorityRevision++; }
        row.LastObservedAt = decision.ObservedAt; row.VerifiedUntil = decision.OwnsProduct ? decision.AuthorizedUntil : null;
        if (decision.OwnsProduct && row.AuthorityKind == LicenseAuthorityKind.VerifiedOwnership) row.ExpiresAt = decision.ProviderExpiresAt;
        return Task.FromResult<GameLicense?>(row);
    }
}
