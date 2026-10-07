using Avalon.Common.GameAuth;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Database.UnitTests;

public sealed class LicenseHoldShould
{
    private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Independent_holds_release_only_the_matching_cause()
    {
        using var database = SqliteDatabase.Auth();
        var grant = await Grant(database);
        var holds = new LicenseHoldRepository(database);
        await holds.SetAsync(grant.Id, "payment-dispute", "one", true, Now);
        await holds.SetAsync(grant.Id, "support", "two", true, Now.AddSeconds(1));
        await holds.SetAsync(grant.Id, "payment-dispute", "one", false, Now.AddSeconds(2));
        var licenses = new GameLicenseRepository(database);
        var held = (await licenses.FindAsync(grant.Id))!;
        Assert.Equal(Now.AddSeconds(1), held.SuspendedAt);
        Assert.False(held.Authorizes(grant.AccountId, "avalon.base", "development", Now.AddSeconds(3)));
        Assert.Null(await licenses.FindActiveAsync(grant.AccountId, "avalon", "development", "avalon.base", "base", Now.AddSeconds(3)));
        var other = await Grant(database, grant.AccountId);
        Assert.True(other.Authorizes(grant.AccountId, "avalon.base", "development", Now));
        await holds.SetAsync(grant.Id, "support", "two", false, Now.AddSeconds(3));
        var restored = (await licenses.FindAsync(grant.Id))!;
        Assert.Null(restored.SuspendedAt);
        Assert.True(restored.Authorizes(grant.AccountId, "avalon.base", "development", Now.AddSeconds(3)));
        Assert.Equal(5, restored.AuthorityRevision);
    }

    [Fact]
    public async Task Duplicate_hold_operations_do_not_bump_revision()
    {
        using var database = SqliteDatabase.Auth();
        var grant = await Grant(database);
        var holds = new LicenseHoldRepository(database);
        var first = await holds.SetAsync(grant.Id, "support", "cause", true, Now);
        var duplicate = await holds.SetAsync(grant.Id, "support", "cause", true, Now.AddSeconds(1));
        Assert.True(first.Changed);
        Assert.False(duplicate.Changed);
        Assert.Equal(first.AuthorityRevision, duplicate.AuthorityRevision);
        var release = await holds.SetAsync(grant.Id, "support", "cause", false, Now.AddSeconds(2));
        var again = await holds.SetAsync(grant.Id, "support", "cause", false, Now.AddSeconds(3));
        Assert.True(release.Changed);
        Assert.False(again.Changed);
        Assert.Equal(release.AuthorityRevision, again.AuthorityRevision);
        await using var db = database.CreateDbContext();
        Assert.Single(await db.LicenseHolds.ToListAsync());
    }

    [Fact]
    public async Task Revocation_is_terminal_despite_hold_release()
    {
        using var database = SqliteDatabase.Auth();
        var grant = await Grant(database);
        var holds = new LicenseHoldRepository(database);
        var held = await holds.SetAsync(grant.Id, "payment-dispute", "cause", true, Now);
        var licenses = new GameLicenseRepository(database);
        var revoked = await licenses.ApplyDecisionAsync(grant.Id, held.AuthorityRevision, new(false, Now.AddSeconds(1), Now.AddSeconds(1)));
        Assert.NotNull(revoked);
        await holds.SetAsync(grant.Id, "payment-dispute", "cause", false, Now.AddSeconds(2));
        var result = (await licenses.FindAsync(grant.Id))!;
        Assert.NotNull(result.RevokedAt);
        Assert.False(result.Authorizes(grant.AccountId, "avalon.base", "development", Now.AddSeconds(3)));
        Assert.Null(await licenses.ApplyDecisionAsync(grant.Id, result.AuthorityRevision, new(true, Now.AddSeconds(3), Now.AddMinutes(5), Reestablish: true)));
    }

    [Fact]
    public async Task Positive_ownership_evidence_does_not_clear_an_independent_hold()
    {
        using var database = SqliteDatabase.Auth();
        var grant = await Grant(database);
        var held = await new LicenseHoldRepository(database).SetAsync(grant.Id, "support", "cause", true, Now);
        var result = await new GameLicenseRepository(database).ApplyDecisionAsync(grant.Id, held.AuthorityRevision, new(true, Now, Now.AddMinutes(5)));
        Assert.NotNull(result!.SuspendedAt);
        Assert.False(result.Authorizes(grant.AccountId, "avalon.base", "development", Now));
    }

    [Fact]
    public async Task Stale_hold_operations_cannot_undo_newer_support_decisions()
    {
        using var database = SqliteDatabase.Auth();
        var grant = await Grant(database);
        var holds = new LicenseHoldRepository(database);
        await holds.SetAsync(grant.Id, "support", "cause", true, Now);
        await holds.SetAsync(grant.Id, "support", "cause", false, Now.AddSeconds(2));
        Assert.False((await holds.SetAsync(grant.Id, "support", "cause", true, Now.AddSeconds(1))).Changed);
        Assert.Null((await new GameLicenseRepository(database).FindAsync(grant.Id))!.SuspendedAt);
    }

    private static async Task<GameLicense> Grant(SqliteDatabase<AuthDbContext> database, Avalon.Common.ValueObjects.AccountId? accountId = null)
    {
        var account = accountId ?? (await PurchaseRepositoryShould.Account(database, "HOLD")).Id;
        return await new GameLicenseRepository(database).RecordGrantAsync(new GameLicense
        {
            Id = Guid.NewGuid(),
            AccountId = account,
            Product = "avalon.base",
            Provider = "avalon",
            Environment = "development",
            ProviderProductId = "base",
            LicenseReference = Guid.NewGuid().ToString("N"),
            AuthorityKind = LicenseAuthorityKind.StoredGrant,
            GrantedAt = Now.AddDays(-1)
        });
    }

    [Fact]
    public async Task Same_transaction_can_stage_multiple_holds_without_losing_earliest_suspension()
    {
        using var database = SqliteDatabase.Auth();
        var grant = await Grant(database);
        await using var db = database.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await LicenseHoldMutations.SetAsync(db, grant.Id, "payment-dispute", "first", true, Now);
        await LicenseHoldMutations.SetAsync(db, grant.Id, "payment-dispute", "second", true, Now.AddSeconds(1));
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        var saved = (await new GameLicenseRepository(database).FindAsync(grant.Id))!;
        Assert.Equal(Now, saved.SuspendedAt);
        Assert.Equal(3, saved.AuthorityRevision);
    }
}
