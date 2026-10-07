using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Avalon.Database.UnitTests;

public class StoreAccountCreationShould
{
    private static readonly DateTime s_now = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Provenance_migration_preserves_existing_Steam_receipts_without_granting_licenses()
    {
        IReadOnlyList<MigrationOperation> operations = new Avalon.Database.Auth.Migrations.SharedStoreProvenance().UpOperations;
        var providers = operations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation>()
            .Where(x => x.Name == "Provider").ToList();
        Assert.Equal(2, providers.Count);
        Assert.All(providers, x => Assert.Equal("steam", x.DefaultValue));
        Assert.DoesNotContain(operations, x => x is Microsoft.EntityFrameworkCore.Migrations.Operations.InsertDataOperation);
    }

    [Fact]
    public async Task Shared_creation_receipts_keep_provider_and_allow_non_Steam_subjects()
    {
        using var database = SqliteDatabase.Auth();
        var repo = new ExternalIdentityRepository(database, new FixedClock(s_now));
        var operation = new StoreAccountCreationOperation(Guid.NewGuid(), StoreAuthenticationModelShould.Account("FUTURE"), "opaque-subject", s_now.AddMinutes(5)) { Provider = "test-store" };
        IdentityLinkResult created = await repo.CreateAccountWithStoreLinkAsync(operation, s_now);
        Assert.Equal(IdentityLinkStatus.Linked, created.Status);
        Assert.Equal("test-store", created.Identity!.Provider);
        Assert.Equal(IdentityLinkStatus.AlreadyLinked, (await repo.CreateAccountWithStoreLinkAsync(operation, s_now)).Status);
        Assert.Equal(IdentityLinkStatus.AuthorityChanged, (await repo.CreateAccountWithStoreLinkAsync(operation with { Provider = "another-store" }, s_now)).Status);
        await using AuthDbContext db = database.CreateDbContext();
        Assert.Equal("test-store", (await db.StoreAccountCreations.SingleAsync()).Provider);
    }

    [Fact]
    public async Task Create_account_and_link_together_and_recover_the_original_operation_once()
    {
        using var database = SqliteDatabase.Auth();
        var repo = new ExternalIdentityRepository(database, new FixedClock(s_now));
        var operation = new StoreAccountCreationOperation(Guid.NewGuid(), StoreAuthenticationModelShould.Account("NEWROOT"), "76561198000000001", s_now.AddMinutes(5));
        IdentityLinkResult created = await repo.CreateAccountWithSteamLinkAsync(operation, s_now);
        Assert.Equal(IdentityLinkStatus.Linked, created.Status);
        Assert.Equal(IdentityLinkStatus.AlreadyLinked, (await repo.CreateAccountWithSteamLinkAsync(operation, s_now)).Status);
        await using AuthDbContext db = database.CreateDbContext();
        Account account = await db.Accounts.SingleAsync(a => a.Username == "NEWROOT");
        ExternalIdentity link = await db.ExternalIdentities.SingleAsync();
        Assert.Equal(account.Id, link.AccountId);
        Assert.Equal(operation.OperationId, link.Id);
        Assert.Equal(1, account.SessionEpoch);
        Assert.Single(await db.StoreAccountCreations.ToListAsync());
    }

    [Fact]
    public async Task Refuse_a_taken_subject_without_creating_an_orphan_account()
    {
        using var database = SqliteDatabase.Auth();
        var repo = new ExternalIdentityRepository(database, new FixedClock(s_now));
        await repo.CreateAccountWithSteamLinkAsync(new(Guid.NewGuid(), StoreAuthenticationModelShould.Account("FIRST"), "76561198000000001", s_now.AddMinutes(5)), s_now);
        Assert.Equal(IdentityLinkStatus.SubjectTaken, (await repo.CreateAccountWithSteamLinkAsync(new(
            Guid.NewGuid(), StoreAuthenticationModelShould.Account("SECOND"), "76561198000000001", s_now.AddMinutes(5)), s_now)).Status);
        await using AuthDbContext db = database.CreateDbContext();
        Assert.False(await db.Accounts.AnyAsync(a => a.Username == "SECOND"));
        Assert.Single(await db.ExternalIdentities.ToListAsync());
    }

    [Fact]
    public async Task Roll_back_the_account_and_link_when_evidence_expires_during_the_write()
    {
        using var database = SqliteDatabase.Auth();
        var repo = new ExternalIdentityRepository(database, new ExpiringClock(s_now));
        IdentityLinkResult result = await repo.CreateAccountWithSteamLinkAsync(new(Guid.NewGuid(),
            StoreAuthenticationModelShould.Account("TOOLATE"), "76561198000000001", s_now.AddMinutes(5)), s_now);
        Assert.Equal(IdentityLinkStatus.AuthorityChanged, result.Status);
        await using AuthDbContext db = database.CreateDbContext();
        Assert.False(await db.Accounts.AnyAsync(a => a.Username == "TOOLATE"));
        Assert.Empty(await db.ExternalIdentities.ToListAsync());
        Assert.Empty(await db.StoreAccountCreations.ToListAsync());
    }

    [Fact]
    public async Task Refuse_to_recreate_a_deleted_account_from_an_old_creation_retry()
    {
        using var database = SqliteDatabase.Auth();
        var repo = new ExternalIdentityRepository(database, new FixedClock(s_now));
        var operation = new StoreAccountCreationOperation(Guid.NewGuid(), StoreAuthenticationModelShould.Account("DELETED"), "76561198000000001", s_now.AddMinutes(5));
        IdentityLinkResult created = await repo.CreateAccountWithSteamLinkAsync(operation, s_now);
        await using (AuthDbContext db = database.CreateDbContext())
            await db.Accounts.Where(a => a.Id == created.Identity!.AccountId).ExecuteDeleteAsync();
        Assert.Equal(IdentityLinkStatus.AuthorityChanged, (await repo.CreateAccountWithSteamLinkAsync(operation, s_now)).Status);
        await using AuthDbContext read = database.CreateDbContext();
        Assert.False(await read.Accounts.AnyAsync(a => a.Username == "DELETED"));
    }

    [Fact]
    public async Task Create_multiple_store_roots_without_inventing_recovery_credentials()
    {
        using var database = SqliteDatabase.Auth();
        var repo = new ExternalIdentityRepository(database, new FixedClock(s_now));
        foreach ((string? name, string? subject) in new[] { ("STOREONE", "76561198000000001"), ("STORETWO", "76561198000000002") })
        {
            var candidate = new Account { Username = name, Email = null, Salt = [], Verifier = [], JoinDate = s_now, IsStoreGenerated = true };
            var operation = new StoreAccountCreationOperation(Guid.NewGuid(), candidate, subject, s_now.AddMinutes(5));
            Assert.Equal(IdentityLinkStatus.Linked, (await repo.CreateAccountWithSteamLinkAsync(operation, s_now)).Status);
            Assert.Equal(IdentityLinkStatus.AlreadyLinked, (await repo.CreateAccountWithSteamLinkAsync(operation, s_now)).Status);
        }
        await using AuthDbContext db = database.CreateDbContext();
        List<Account> roots = await db.Accounts.Where(a => a.Username.StartsWith("STORE")).ToListAsync();
        Assert.Equal(2, roots.Count);
        Assert.All(roots, a => { Assert.Null(a.Email); Assert.Empty(a.Salt); Assert.Empty(a.Verifier); });
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
    private sealed class ExpiringClock(DateTime now) : TimeProvider
    {
        private int _calls;
        public override DateTimeOffset GetUtcNow() => new(++_calls == 1 ? now : now.AddMinutes(5));
    }
}
