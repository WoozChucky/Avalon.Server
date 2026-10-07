using Avalon.Common.GameAuth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Xunit;

namespace Avalon.Database.UnitTests;

public sealed class GameLicenseShould
{
    private static readonly DateTime s_now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("steam")]
    [InlineData("avalon")]
    [InlineData("test-store")]
    public async Task Provider_and_environment_scope_reference_uniqueness(string provider)
    {
        using var db = SqliteDatabase.Auth();
        Account account = await new AccountRepository(db).CreateAsync(StoreAuthenticationModelShould.Account("LICENSE"));
        var repo = new GameLicenseRepository(db);
        GameLicense grant = Grant(account.Id, provider);
        Assert.Equal(grant.Id, (await repo.RecordGrantAsync(grant, default)).Id);
        GameLicense same = Grant(account.Id, provider);
        Assert.Equal(grant.Id, (await repo.RecordGrantAsync(same, default)).Id);
        GameLicense otherProvider = Grant(account.Id, "other-store");
        Assert.Equal(otherProvider.Id, (await repo.RecordGrantAsync(otherProvider, default)).Id);
        GameLicense otherEnvironment = Grant(account.Id, provider); otherEnvironment.Environment = "test";
        Assert.Equal(otherEnvironment.Id, (await repo.RecordGrantAsync(otherEnvironment, default)).Id);
        Assert.Equal(grant.Id, (await repo.FindActiveAsync(account.Id, provider, "production", "avalon.base", "base", s_now, default))!.Id);
        GameLicense conflict = Grant(account.Id, provider); conflict.Product = "other-product";
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.RecordGrantAsync(conflict, default));
    }

    [Fact]
    public async Task Missing_future_expired_or_revoked_license_is_inactive()
    {
        using var db = SqliteDatabase.Auth();
        Account account = await new AccountRepository(db).CreateAsync(StoreAuthenticationModelShould.Account("TIME"));
        var repo = new GameLicenseRepository(db);
        Assert.Null(await repo.FindActiveAsync(account.Id, "avalon", "production", "avalon.base", "base", s_now, default));
        GameLicense grant = Grant(account.Id, "avalon"); grant.GrantedAt = s_now.AddMinutes(1);
        await repo.RecordGrantAsync(grant, default);
        Assert.Null(await repo.FindActiveAsync(account.Id, "avalon", "production", "avalon.base", "base", s_now, default));
        GameLicense expired = Grant(account.Id, "avalon"); expired.LicenseReference = "expired"; expired.ExpiresAt = s_now;
        await repo.RecordGrantAsync(expired, default);
        Assert.False(expired.Authorizes(account.Id, "avalon.base", "production", s_now));
        GameLicense revoked = Grant(account.Id, "avalon"); revoked.LicenseReference = "revoked"; revoked.RevokedAt = s_now.AddHours(1);
        await repo.RecordGrantAsync(revoked, default);
        Assert.False(revoked.Authorizes(account.Id, "avalon.base", "production", s_now));
    }

    [Fact]
    public async Task Revision_conflicts_and_old_evidence_cannot_revive_a_revoked_purchase()
    {
        using var db = SqliteDatabase.Auth();
        Account account = await new AccountRepository(db).CreateAsync(StoreAuthenticationModelShould.Account("REVOKE"));
        var repo = new GameLicenseRepository(db);
        GameLicense grant = await repo.RecordGrantAsync(Grant(account.Id, "avalon"), default);
        GameLicense? revoked = await repo.ApplyDecisionAsync(grant.Id, grant.AuthorityRevision,
            new LicenseAuthorityDecision(false, s_now, s_now), default);
        Assert.NotNull(revoked);
        Assert.Equal(2, revoked.AuthorityRevision);
        Assert.False(revoked.Authorizes(account.Id, "avalon.base", "production", s_now));
        Assert.Null(await repo.ApplyDecisionAsync(grant.Id, 1, new(true, s_now.AddSeconds(1), s_now.AddMinutes(5)), default));
        Assert.Null(await repo.ApplyDecisionAsync(grant.Id, 2, new(true, s_now.AddSeconds(1), s_now.AddMinutes(5), reestablish: true), default));
    }

    [Fact]
    public async Task Verified_ownership_can_establish_a_new_revision_only_from_fresh_evidence()
    {
        using var db = SqliteDatabase.Auth();
        Account account = await new AccountRepository(db).CreateAsync(StoreAuthenticationModelShould.Account("STORE"));
        var repo = new GameLicenseRepository(db);
        GameLicense grant = Grant(account.Id, "test-store"); grant.AuthorityKind = LicenseAuthorityKind.VerifiedOwnership;
        await repo.RecordGrantAsync(grant, default);
        Assert.NotNull(await repo.ApplyDecisionAsync(grant.Id, 1, new(false, s_now, s_now), default));
        Assert.Null(await repo.ApplyDecisionAsync(grant.Id, 2, new(true, s_now.AddSeconds(-1), s_now.AddMinutes(5).AddSeconds(-1), reestablish: true), default));
        GameLicense? renewed = await repo.ApplyDecisionAsync(grant.Id, 2, new(true, s_now.AddSeconds(1), s_now.AddMinutes(5), reestablish: true), default);
        Assert.NotNull(renewed); Assert.Equal(3, renewed.AuthorityRevision);
        Assert.Null(renewed.RevokedAt);
        Assert.NotNull(await repo.ApplyDecisionAsync(grant.Id, 3, new(true, s_now.AddSeconds(2), s_now.AddMinutes(5)), default));
        Assert.Equal(3, (await repo.FindAsync(grant.Id, default))!.AuthorityRevision);
    }

    [Fact]
    public async Task Negative_provider_evidence_can_revoke_after_its_reported_expiry_predates_the_grant()
    {
        using var db = SqliteDatabase.Auth();
        Account account = await new AccountRepository(db).CreateAsync(StoreAuthenticationModelShould.Account("EXPIREDSTORE"));
        var repo = new GameLicenseRepository(db);
        GameLicense grant = Grant(account.Id, "test-store");
        grant.AuthorityKind = LicenseAuthorityKind.VerifiedOwnership;
        grant.ExpiresAt = s_now.AddDays(1);
        await repo.RecordGrantAsync(grant);

        DateTime providerExpiry = grant.GrantedAt.AddDays(-1);
        GameLicense? revoked = await repo.ApplyDecisionAsync(grant.Id, 1,
            new(false, s_now, providerExpiry, providerExpiresAt: providerExpiry));

        Assert.NotNull(revoked);
        Assert.Equal(2, revoked.AuthorityRevision);
        Assert.Equal(s_now, revoked.RevokedAt);
        Assert.Equal(s_now.AddDays(1), revoked.ExpiresAt);
        Assert.Null(revoked.VerifiedUntil);
        Assert.False(revoked.Authorizes(account.Id, "avalon.base", "production", s_now));
        Assert.Equal(2, (await repo.FindAsync(grant.Id))!.AuthorityRevision);
    }

    private static GameLicense Grant(Avalon.Common.ValueObjects.AccountId account, string provider) => new()
    {
        Id = Guid.NewGuid(),
        AccountId = account,
        Provider = provider,
        Environment = "production",
        Product = "avalon.base",
        ProviderProductId = "base",
        LicenseReference = "proof-1",
        AuthorityKind = LicenseAuthorityKind.StoredGrant,
        GrantedAt = s_now.AddDays(-1),
        AuthorityRevision = 1,
    };

    [Theory]
    [InlineData("steam")]
    [InlineData("avalon")]
    [InlineData("test-store")]
    public async Task Conflicting_grant_cannot_change_account_or_source(string provider)
    {
        using var db = SqliteDatabase.Auth();
        var accounts = new AccountRepository(db);
        Account one = await accounts.CreateAsync(StoreAuthenticationModelShould.Account("ONE"));
        Account two = await accounts.CreateAsync(StoreAuthenticationModelShould.Account("TWO"));
        var repo = new GameLicenseRepository(db);
        GameLicense original = await repo.RecordGrantAsync(Grant(one.Id, provider));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.RecordGrantAsync(Grant(two.Id, provider)));
        GameLicense differentApplication = Grant(one.Id, provider); differentApplication.ProviderProductId = "other";
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.RecordGrantAsync(differentApplication));
        GameLicense differentSubject = Grant(one.Id, provider); differentSubject.ProviderSubject = "different";
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.RecordGrantAsync(differentSubject));
        Assert.Equal(one.Id, (await repo.FindAsync(original.Id))!.AccountId);
    }

    [Fact]
    public async Task Old_observations_do_not_create_game_licenses()
    {
        using var db = SqliteDatabase.Auth();
        Account account = await new AccountRepository(db).CreateAsync(StoreAuthenticationModelShould.Account("AUDIT"));
        await new LicenseObservationRepository(db).RecordAsync(new LicenseObservation
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Provider = "steam",
            ProviderSubject = "subject",
            Environment = "production",
            Product = "avalon.base",
            ProviderProductId = "base",
            OwnsProduct = true,
            ObservedAt = s_now,
            AuthorizedUntil = s_now.AddMinutes(5),
            PolicyVersion = 1,
        });
        Assert.Null(await new GameLicenseRepository(db).FindActiveAsync(account.Id, "steam", "production", "avalon.base", "base", s_now));
    }
}
