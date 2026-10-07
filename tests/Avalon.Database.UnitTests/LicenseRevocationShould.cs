using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Xunit;

namespace Avalon.Database.UnitTests;

public sealed class LicenseRevocationShould
{
    [Theory]
    [InlineData("2499460", "2514590")]
    [InlineData("2514590", "2499460")]
    public async Task Negative_evidence_and_latest_observations_are_application_scoped(string active, string other)
    {
        using var db = SqliteDatabase.Auth();
        var root = await new AccountRepository(db).CreateAsync(StoreAuthenticationModelShould.Account("SCOPED"));
        var repo = new LicenseObservationRepository(db);
        var now = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
        await repo.RecordAsync(Observation(root.Id, true, now, active));
        await repo.RecordAsync(Observation(root.Id, false, now.AddMinutes(1), other));
        Assert.False(await repo.HasNegativeSinceAsync(root.Id, "steam", "76561198000000001", "production", "avalon.base", active, now));
        Assert.True(await repo.HasNegativeSinceAsync(root.Id, "steam", "76561198000000001", "production", "avalon.base", other, now));
        var latest = (await repo.FindLatestAsync(root.Id, "steam", "76561198000000001", "production", "avalon.base", active))!;
        Assert.True(latest.OwnsProduct);
        Assert.Equal(active, latest.ProviderProductId);
        await repo.RecordAsync(Observation(root.Id, true, now.AddMinutes(2), other));
        Assert.True(await repo.HasNegativeSinceAsync(root.Id, "steam", "76561198000000001", "production", "avalon.base", other, now));
    }

    [Fact]
    public async Task A_later_positive_observation_does_not_resurrect_a_grant_that_predates_a_negative()
    {
        using var db = SqliteDatabase.Auth();
        var root = await new AccountRepository(db).CreateAsync(StoreAuthenticationModelShould.Account("NEGATIVE"));
        var repo = new LicenseObservationRepository(db);
        var now = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
        await repo.RecordAsync(Observation(root.Id, true, now));
        Assert.False(await repo.HasNegativeSinceAsync(root.Id, "steam", "76561198000000001", "production", "avalon.base", "2499460", now));
        await repo.RecordAsync(Observation(root.Id, false, now.AddMinutes(1)));
        await repo.RecordAsync(Observation(root.Id, true, now.AddMinutes(2)));
        Assert.True((await repo.FindLatestAsync(root.Id, "steam", "76561198000000001", "production", "avalon.base", "2499460"))!.OwnsProduct);
        Assert.True(await repo.HasNegativeSinceAsync(root.Id, "steam", "76561198000000001", "production", "avalon.base", "2499460", now));
        Assert.False(await repo.HasNegativeSinceAsync(root.Id, "steam", "76561198000000001", "production", "avalon.base", "2499460", now.AddMinutes(2)));
        Assert.False(await repo.HasNegativeSinceAsync(root.Id, "steam", "76561198000000002", "production", "avalon.base", "2499460", now));
        Assert.False(await repo.HasNegativeSinceAsync(root.Id, "steam", "76561198000000001", "staging", "avalon.base", "2499460", now));
        Assert.False(await repo.HasNegativeSinceAsync(root.Id, "steam", "76561198000000001", "production", "other-product", "2499460", now));
    }
    private static LicenseObservation Observation(AccountId account, bool owned, DateTime at, string appId = "2499460") => new()
    {
        Id = Guid.NewGuid(),
        AccountId = account,
        Provider = "steam",
        ProviderSubject = "76561198000000001",
        Environment = "production",
        Product = "avalon.base",
        ProviderProductId = appId,
        OwnsProduct = owned,
        ObservedAt = at,
        AuthorizedUntil = owned ? at.AddMinutes(5) : at,
        PolicyVersion = 1
    };
}
