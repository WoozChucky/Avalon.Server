using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Xunit;

namespace Avalon.Database.UnitTests;

public sealed class LicenseRevocationShould
{
    [Fact]
    public async Task A_later_positive_observation_does_not_resurrect_a_grant_that_predates_a_negative()
    {
        using var db = SqliteDatabase.Auth();
        var root = await new AccountRepository(db).CreateAsync(StoreAuthenticationModelShould.Account("NEGATIVE"));
        var repo = new LicenseObservationRepository(db);
        var now = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
        await repo.RecordAsync(Observation(root.Id, true, now));
        Assert.False(await repo.HasNegativeSinceAsync(root.Id, "steam", "76561198000000001", "production", "avalon.base", now));
        await repo.RecordAsync(Observation(root.Id, false, now.AddMinutes(1)));
        await repo.RecordAsync(Observation(root.Id, true, now.AddMinutes(2)));
        Assert.True((await repo.FindLatestAsync(root.Id, "steam", "76561198000000001", "production", "avalon.base"))!.OwnsProduct);
        Assert.True(await repo.HasNegativeSinceAsync(root.Id, "steam", "76561198000000001", "production", "avalon.base", now));
        Assert.False(await repo.HasNegativeSinceAsync(root.Id, "steam", "76561198000000001", "production", "avalon.base", now.AddMinutes(2)));
        Assert.False(await repo.HasNegativeSinceAsync(root.Id, "steam", "76561198000000002", "production", "avalon.base", now));
        Assert.False(await repo.HasNegativeSinceAsync(root.Id, "steam", "76561198000000001", "staging", "avalon.base", now));
        Assert.False(await repo.HasNegativeSinceAsync(root.Id, "steam", "76561198000000001", "production", "other-product", now));
    }
    private static LicenseObservation Observation(AccountId account, bool owned, DateTime at) => new()
    {
        Id = Guid.NewGuid(), AccountId = account, Provider = "steam", ProviderSubject = "76561198000000001",
        Environment = "production", Product = "avalon.base", ProviderAppId = "2499460", OwnsProduct = owned,
        ObservedAt = at, AuthorizedUntil = owned ? at.AddMinutes(5) : at, PolicyVersion = 1
    };
}
