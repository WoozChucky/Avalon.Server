using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Database.UnitTests;

public sealed class AccountConsolidationShould
{
    [Fact]
    public async Task Consolidation_moves_beneficiary_and_preserves_original_purchaser()
    {
        using var database = SqliteDatabase.Auth();
        var (source, target, request) = await Seed(database);
        await VerifyPurchaseAccounts(database, source.Id, target.Id);
        var purchases = new PurchaseRepository(database, _clock);
        var purchase = await purchases.ReserveAsync(PurchaseRepositoryShould.Reservation(source.Id));
        var operationKey = purchase.Attempt!.OperationKey;
        var repository = new AccountConsolidationRepository(database, _clock);
        Assert.Null((await repository.BeginAsync(request, default)).Error);
        foreach (var world in request.Worlds) Assert.True(await repository.RecordTransferAsync(request.OperationId, world, 0, default));
        Assert.True(await repository.FinalizeAsync(request.OperationId, default));
        Assert.Null(await purchases.FindForAccountAsync(source.Id, purchase.Order!.Id));
        var moved = await purchases.FindForAccountAsync(target.Id, purchase.Order.Id);
        Assert.Equal(source.Id, moved!.OriginalPurchaserAccountId);
        Assert.Equal(target.Id, moved.AccountId);
        Assert.Equal(800, moved.AmountMinor);
        Assert.Equal("eur", moved.Currency);
        await using var read = database.CreateDbContext();
        Assert.Equal(operationKey, (await read.PaymentAttempts.SingleAsync()).OperationKey);
    }

    [Fact]
    public async Task Conflicting_unresolved_orders_block_consolidation_before_character_transfers()
    {
        using var database = SqliteDatabase.Auth();
        var (source, target, request) = await Seed(database);
        await VerifyPurchaseAccounts(database, source.Id, target.Id);
        var purchases = new PurchaseRepository(database, _clock);
        Assert.Null((await purchases.ReserveAsync(PurchaseRepositoryShould.Reservation(source.Id))).Error);
        Assert.Null((await purchases.ReserveAsync(PurchaseRepositoryShould.Reservation(target.Id))).Error);
        var result = await new AccountConsolidationRepository(database, _clock).BeginAsync(request, default);
        Assert.NotNull(result.Error);
        await using var db = database.CreateDbContext();
        Assert.Empty(await db.AccountConsolidations.ToListAsync());
        Assert.All(await db.Accounts.Where(x => x.Id == source.Id || x.Id == target.Id).ToListAsync(), x => Assert.Null(x.GameplayConsolidationId));
    }

    private async Task VerifyPurchaseAccounts(SqliteDatabase<Avalon.Database.Auth.AuthDbContext> database, AccountId source, AccountId target)
    {
        await using var db = database.CreateDbContext();
        var sourceRow = await db.Accounts.SingleAsync(x => x.Id == source);
        sourceRow.Email = "store-purchase@example.test";
        sourceRow.EmailVerifiedAt = _clock.GetUtcNow().UtcDateTime;
        var targetRow = await db.Accounts.SingleAsync(x => x.Id == target);
        targetRow.EmailVerifiedAt = _clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
    }
    [Fact]
    public void Add_consolidation_schema_without_recreating_existing_accounts()
    {
        var migration = new Avalon.Database.Auth.Migrations.StoreAccountConsolidation();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is
            Microsoft.EntityFrameworkCore.Migrations.Operations.DeleteDataOperation { Table: "Accounts" } or
            Microsoft.EntityFrameworkCore.Migrations.Operations.InsertDataOperation { Table: "Accounts" });
    }

    private const string SteamId = "76561198000000001";

    [Fact]
    public async Task RetainsTargetEmailProofAndInvalidatesSourceChallenges()
    {
        using var database = SqliteDatabase.Auth();
        var (source, target, request) = await Seed(database);
        var now = _clock.GetUtcNow().UtcDateTime;
        await using (var db = database.CreateDbContext())
        {
            await db.Accounts.Where(a => a.Id == target.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.EmailVerifiedAt, (DateTime?)now));
            db.AccountEmailVerifications.Add(new AccountEmailVerification
            {
                AccountId = source.Id,
                TokenHash = new string('a', 64),
                Email = "prior@example.test",
                IssuedAt = now,
                ExpiresAt = now.AddMinutes(30)
            });
            await db.SaveChangesAsync();
        }
        var repository = new AccountConsolidationRepository(database, _clock);
        Assert.Null((await repository.BeginAsync(request, default)).Error);
        foreach (var world in request.Worlds) Assert.True(await repository.RecordTransferAsync(request.OperationId, world, 0, default));
        Assert.True(await repository.FinalizeAsync(request.OperationId, default));
        await using var read = database.CreateDbContext();
        Assert.Equal(now, (await read.Accounts.SingleAsync(a => a.Id == target.Id)).EmailVerifiedAt);
        Assert.NotNull((await read.AccountEmailVerifications.SingleAsync()).InvalidatedAt);
    }

    [Fact]
    public async Task Transfer_all_shared_licenses_once_preserving_sources_and_invalidating_prior_authority()
    {
        using var database = SqliteDatabase.Auth();
        var (source, target, request) = await Seed(database);
        var now = _clock.GetUtcNow().UtcDateTime;
        await using (var db = database.CreateDbContext())
        {
            foreach (var provider in new[] { "steam", "avalon", "test-store" })
                db.GameLicenses.Add(new GameLicense
                {
                    Id = Guid.NewGuid(),
                    AccountId = source.Id,
                    Provider = provider,
                    Product = "avalon.base",
                    Environment = "production",
                    ProviderProductId = "base",
                    LicenseReference = provider + "-grant",
                    GrantedAt = now,
                    AuthorityKind = provider == "avalon" ? Avalon.Common.GameAuth.LicenseAuthorityKind.StoredGrant : Avalon.Common.GameAuth.LicenseAuthorityKind.VerifiedOwnership,
                    LastObservedAt = now,
                    VerifiedUntil = now.AddMinutes(5)
                });
            await db.SaveChangesAsync();
        }
        var repository = new AccountConsolidationRepository(database, _clock);
        Assert.Null((await repository.BeginAsync(request, default)).Error);
        foreach (var world in request.Worlds) Assert.True(await repository.RecordTransferAsync(request.OperationId, world, 0, default));
        Assert.True(await repository.FinalizeAsync(request.OperationId, default));
        Assert.True(await repository.FinalizeAsync(request.OperationId, default));
        await using var read = database.CreateDbContext();
        var rows = await read.GameLicenses.OrderBy(x => x.Provider).ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(target.Id, row.AccountId); Assert.Equal(2, row.AuthorityRevision);
            Assert.Equal(row.Provider + "-grant", row.LicenseReference); Assert.Null(row.VerifiedUntil); Assert.Null(row.RevokedAt);
        });
        Assert.True(rows.Single(x => x.Provider == "avalon").Authorizes(target.Id, "avalon.base", "production", now));
        Assert.False(rows.Single(x => x.Provider == "steam").Authorizes(target.Id, "avalon.base", "production", now));
    }

    [Fact]
    public async Task Consolidation_records_the_verified_provider_and_subject()
    {
        using var database = SqliteDatabase.Auth();
        var (source, target, request) = await Seed(database);
        await using (var db = database.CreateDbContext())
        {
            var identity = await db.ExternalIdentities.SingleAsync();
            identity.Provider = "test-store"; identity.ProviderSubject = "opaque-subject";
            await db.SaveChangesAsync();
        }
        var result = await new AccountConsolidationRepository(database, _clock).BeginAsync(request with
        { Provider = "test-store", ProviderSubject = "opaque-subject" }, default);
        Assert.Null(result.Error);
        Assert.Equal("test-store", result.Operation!.Provider);
        Assert.Equal("opaque-subject", result.Operation.ProviderSubject);
    }
    private readonly ManualClock _clock = new(DateTimeOffset.UtcNow);
    private async Task<(Account Source, Account Target, AccountConsolidationRequest Request)> Seed(SqliteDatabase<Avalon.Database.Auth.AuthDbContext> database)
    {
        var accounts = new AccountRepository(database);
        var source = await accounts.CreateAsync(new Account { Username = "AUTOMATICSTEAM", Email = null, IsStoreGenerated = true, Salt = [], Verifier = [], JoinDate = _clock.GetUtcNow().UtcDateTime });
        var target = await accounts.CreateAsync(StoreAuthenticationModelShould.Account("WEBSURVIVOR"));
        await new ExternalIdentityRepository(database, _clock).LinkAsync(source.Id, "steam", SteamId, _clock.GetUtcNow().UtcDateTime);
        var request = new AccountConsolidationRequest(Guid.NewGuid(), target.Id, SteamId, target.CredentialsVersion, target.SessionEpoch, null,
            [new WorldId(1), new WorldId(2)], _clock.GetUtcNow().UtcDateTime.AddMinutes(2));
        return (source, target, request);
    }

    [Fact]
    public async Task Freeze_both_roots_atomically_and_resume_the_durable_intent_after_proof_expiry()
    {
        using var database = SqliteDatabase.Auth();
        var (source, target, request) = await Seed(database);
        var repository = new AccountConsolidationRepository(database, _clock);
        var result = await repository.BeginAsync(request, CancellationToken.None);
        Assert.Null(result.Error);
        Assert.Equal(request.OperationId, (await repository.FindPendingForTargetAsync(target.Id, CancellationToken.None))!.Id);
        Assert.Null(await repository.FindPendingForTargetAsync(source.Id, CancellationToken.None));
        Assert.Equal(source.Id, result.Operation!.SourceAccountId);
        Assert.Equal(target.Id, result.Operation.TargetAccountId);
        Assert.Equal(new ushort[] { 1, 2 }, result.Operation.Worlds.OrderBy(w => w.WorldId).Select(w => w.WorldId));
        var accounts = new AccountRepository(database);
        foreach (var accountId in new[] { source.Id, target.Id })
        {
            var root = (await accounts.FindByIdAsync(accountId))!;
            Assert.Equal(request.OperationId, root.GameplayConsolidationId);
            Assert.Equal(1, root.SessionEpoch);
        }
        _clock.Advance(TimeSpan.FromMinutes(3));
        Assert.Equal(request.OperationId, (await repository.BeginAsync(request, CancellationToken.None)).Operation!.Id);
        Assert.Equal(1, (await accounts.FindByIdAsync(target.Id))!.SessionEpoch);
        Assert.False(await repository.FinalizeAsync(request.OperationId, CancellationToken.None));
    }

    [Fact]
    public async Task Reassign_the_link_and_retire_only_the_automatic_root_after_every_world_commits()
    {
        using var database = SqliteDatabase.Auth();
        var (source, target, request) = await Seed(database);
        var repository = new AccountConsolidationRepository(database, _clock);
        Assert.Null((await repository.BeginAsync(request, CancellationToken.None)).Error);
        Assert.False(await repository.RecordTransferAsync(request.OperationId, new WorldId(3), 7, CancellationToken.None));
        Assert.True(await repository.RecordTransferAsync(request.OperationId, new WorldId(1), 5, CancellationToken.None));
        Assert.False(await repository.FinalizeAsync(request.OperationId, CancellationToken.None));
        Assert.True(await repository.RecordTransferAsync(request.OperationId, new WorldId(2), 8, CancellationToken.None));
        Assert.True(await repository.FinalizeAsync(request.OperationId, CancellationToken.None));
        Assert.True(await repository.FinalizeAsync(request.OperationId, CancellationToken.None));
        var accounts = new AccountRepository(database);
        var retired = (await accounts.FindByIdAsync(source.Id))!;
        var survivor = (await accounts.FindByIdAsync(target.Id))!;
        Assert.Equal(AccountStatus.Deactivated, retired.Status);
        Assert.NotNull(retired.GameplayConsolidationId);
        Assert.Null(survivor.GameplayConsolidationId);
        Assert.Equal(2, survivor.SessionEpoch);
        Assert.Equal(target.Id, (await new ExternalIdentityRepository(database).FindAsync("steam", SteamId))!.AccountId);
        Assert.Equal(AccountConsolidationState.Finalized, (await repository.FindAsync(request.OperationId, CancellationToken.None))!.State);
    }

    [Theory]
    [InlineData("banned")]
    [InlineData("privileged")]
    [InlineData("manual-source")]
    [InlineData("stale-target")]
    [InlineData("expired-proof")]
    public async Task Reject_restricted_sources_and_stale_target_authority_without_freezing_either_root(string condition)
    {
        using var database = SqliteDatabase.Auth();
        var (source, target, request) = await Seed(database);
        await using (var db = database.CreateDbContext())
        {
            var root = await db.Accounts.SingleAsync(a => a.Id == source.Id);
            if (condition == "banned") root.Status = AccountStatus.Banned;
            if (condition == "privileged") root.AccessLevel = AccountAccessLevel.Admin;
            if (condition == "manual-source") await db.Accounts.Where(a => a.Id == source.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.IsStoreGenerated, false));
            await db.SaveChangesAsync();
        }
        if (condition == "stale-target") request = request with { CredentialsVersion = 1 };
        if (condition == "expired-proof") _clock.Advance(TimeSpan.FromMinutes(3));
        Assert.NotNull((await new AccountConsolidationRepository(database, _clock).BeginAsync(request, CancellationToken.None)).Error);
        var accounts = new AccountRepository(database);
        Assert.Null((await accounts.FindByIdAsync(source.Id))!.GameplayConsolidationId);
        Assert.Null((await accounts.FindByIdAsync(target.Id))!.GameplayConsolidationId);
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan span) => _now += span;
    }
}
