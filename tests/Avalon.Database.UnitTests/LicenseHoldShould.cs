using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Avalon.Database.UnitTests;

public sealed class LicenseHoldShould
{
    private static readonly DateTime s_now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Independent_holds_release_only_the_matching_cause()
    {
        using var database = SqliteDatabase.Auth();
        GameLicense grant = await Grant(database);
        var holds = new LicenseHoldRepository(database);
        await holds.SetAsync(grant.Id, "payment-dispute", "one", true, s_now);
        await holds.SetAsync(grant.Id, "support", "two", true, s_now.AddSeconds(1));
        await holds.SetAsync(grant.Id, "payment-dispute", "one", false, s_now.AddSeconds(2));
        var licenses = new GameLicenseRepository(database);
        GameLicense held = (await licenses.FindAsync(grant.Id))!;
        Assert.Equal(s_now.AddSeconds(1), held.SuspendedAt);
        Assert.False(held.Authorizes(grant.AccountId, "avalon.base", "development", s_now.AddSeconds(3)));
        Assert.Null(await licenses.FindActiveAsync(grant.AccountId, "avalon", "development", "avalon.base", "base", s_now.AddSeconds(3)));
        GameLicense other = await Grant(database, grant.AccountId);
        Assert.True(other.Authorizes(grant.AccountId, "avalon.base", "development", s_now));
        await holds.SetAsync(grant.Id, "support", "two", false, s_now.AddSeconds(3));
        GameLicense restored = (await licenses.FindAsync(grant.Id))!;
        Assert.Null(restored.SuspendedAt);
        Assert.True(restored.Authorizes(grant.AccountId, "avalon.base", "development", s_now.AddSeconds(3)));
        Assert.Equal(5, restored.AuthorityRevision);
    }

    [Fact]
    public async Task Duplicate_hold_operations_do_not_bump_revision()
    {
        using var database = SqliteDatabase.Auth();
        GameLicense grant = await Grant(database);
        var holds = new LicenseHoldRepository(database);
        LicenseHoldResult first = await holds.SetAsync(grant.Id, "support", "cause", true, s_now);
        LicenseHoldResult duplicate = await holds.SetAsync(grant.Id, "support", "cause", true, s_now.AddSeconds(1));
        Assert.True(first.Changed);
        Assert.False(duplicate.Changed);
        Assert.Equal(first.AuthorityRevision, duplicate.AuthorityRevision);
        LicenseHoldResult release = await holds.SetAsync(grant.Id, "support", "cause", false, s_now.AddSeconds(2));
        LicenseHoldResult again = await holds.SetAsync(grant.Id, "support", "cause", false, s_now.AddSeconds(3));
        Assert.True(release.Changed);
        Assert.False(again.Changed);
        Assert.Equal(release.AuthorityRevision, again.AuthorityRevision);
        await using AuthDbContext db = database.CreateDbContext();
        Assert.Single(await db.LicenseHolds.ToListAsync());
    }

    [Fact]
    public async Task Revocation_is_terminal_despite_hold_release()
    {
        using var database = SqliteDatabase.Auth();
        GameLicense grant = await Grant(database);
        var holds = new LicenseHoldRepository(database);
        LicenseHoldResult held = await holds.SetAsync(grant.Id, "payment-dispute", "cause", true, s_now);
        var licenses = new GameLicenseRepository(database);
        GameLicense? revoked = (await licenses.ApplyDecisionAsync(grant.Id, held.AuthorityRevision, new(false, s_now.AddSeconds(1), s_now.AddSeconds(1)))).License;
        Assert.NotNull(revoked);
        await holds.SetAsync(grant.Id, "payment-dispute", "cause", false, s_now.AddSeconds(2));
        GameLicense result = (await licenses.FindAsync(grant.Id))!;
        Assert.NotNull(result.RevokedAt);
        Assert.False(result.Authorizes(grant.AccountId, "avalon.base", "development", s_now.AddSeconds(3)));
        Assert.Null((await licenses.ApplyDecisionAsync(grant.Id, result.AuthorityRevision, new(true, s_now.AddSeconds(3), s_now.AddMinutes(5), reestablish: true))).License);
    }

    [Fact]
    public async Task Positive_ownership_evidence_does_not_clear_an_independent_hold()
    {
        using var database = SqliteDatabase.Auth();
        GameLicense grant = await Grant(database);
        LicenseHoldResult held = await new LicenseHoldRepository(database).SetAsync(grant.Id, "support", "cause", true, s_now);
        GameLicense? result = (await new GameLicenseRepository(database).ApplyDecisionAsync(grant.Id, held.AuthorityRevision, new(true, s_now, s_now.AddMinutes(5)))).License;
        Assert.NotNull(result!.SuspendedAt);
        Assert.False(result.Authorizes(grant.AccountId, "avalon.base", "development", s_now));
    }

    [Fact]
    public async Task Stale_hold_operations_cannot_undo_newer_support_decisions()
    {
        using var database = SqliteDatabase.Auth();
        GameLicense grant = await Grant(database);
        var holds = new LicenseHoldRepository(database);
        await holds.SetAsync(grant.Id, "support", "cause", true, s_now);
        await holds.SetAsync(grant.Id, "support", "cause", false, s_now.AddSeconds(2));
        Assert.False((await holds.SetAsync(grant.Id, "support", "cause", true, s_now.AddSeconds(1))).Changed);
        Assert.Null((await new GameLicenseRepository(database).FindAsync(grant.Id))!.SuspendedAt);
    }

    private static async Task<GameLicense> Grant(SqliteDatabase<AuthDbContext> database, Avalon.Common.ValueObjects.AccountId? accountId = null)
    {
        AccountId account = accountId ?? (await PurchaseRepositoryShould.Account(database, "HOLD")).Id;
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
            GrantedAt = s_now.AddDays(-1)
        });
    }

    [Fact]
    public async Task Same_transaction_can_stage_multiple_holds_without_losing_earliest_suspension()
    {
        using var database = SqliteDatabase.Auth();
        GameLicense grant = await Grant(database);
        await using AuthDbContext db = database.CreateDbContext();
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
        await LicenseHoldMutations.SetAsync(db, grant.Id, "payment-dispute", "first", true, s_now);
        await LicenseHoldMutations.SetAsync(db, grant.Id, "payment-dispute", "second", true, s_now.AddSeconds(1));
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        GameLicense saved = (await new GameLicenseRepository(database).FindAsync(grant.Id))!;
        Assert.Equal(s_now, saved.SuspendedAt);
        Assert.Equal(3, saved.AuthorityRevision);
    }
}
