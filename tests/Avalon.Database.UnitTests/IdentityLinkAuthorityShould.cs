using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Database.UnitTests;

public class IdentityLinkAuthorityShould
{
    private static readonly DateTime s_now = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Commit_identity_and_epoch_together_and_allow_only_the_original_operation_to_retry()
    {
        using var database = SqliteDatabase.Auth();
        Account account = await new AccountRepository(database).CreateAsync(StoreAuthenticationModelShould.Account("LINK"));
        var repo = new ExternalIdentityRepository(database, new FixedClock(s_now));
        var operation = new IdentityLinkOperation(Guid.NewGuid(), account.Id, "steam", "76561198000000001", 0, 0, null) { ProofExpiresAt = s_now.AddMinutes(5) };
        Assert.Equal(IdentityLinkStatus.Linked, (await repo.LinkWithAuthorityAsync(operation, s_now)).Status);
        Assert.Equal(IdentityLinkStatus.AlreadyLinked, (await repo.LinkWithAuthorityAsync(operation, s_now)).Status);
        Assert.Equal(IdentityLinkStatus.AuthorityChanged, (await repo.LinkWithAuthorityAsync(operation with { OperationId = Guid.NewGuid() }, s_now)).Status);
        await using AuthDbContext db = database.CreateDbContext();
        Assert.Equal(1, (await db.Accounts.SingleAsync(a => a.Id == account.Id)).SessionEpoch);
        Assert.Equal(operation.OperationId, (await db.ExternalIdentities.SingleAsync()).Id);
    }

    [Fact]
    public async Task Refuse_stale_password_epoch_or_MFA_state_without_inserting_an_identity()
    {
        using var database = SqliteDatabase.Auth();
        Account account = await new AccountRepository(database).CreateAsync(StoreAuthenticationModelShould.Account("STALE"));
        var repo = new ExternalIdentityRepository(database, new FixedClock(s_now));
        var operation = new IdentityLinkOperation(Guid.NewGuid(), account.Id, "steam", "76561198000000001", 0, 0, null) { ProofExpiresAt = s_now.AddMinutes(5) };
        Assert.Equal(IdentityLinkStatus.AuthorityChanged, (await repo.LinkWithAuthorityAsync(operation with { CredentialsVersion = 1 }, s_now)).Status);
        Assert.Equal(IdentityLinkStatus.AuthorityChanged, (await repo.LinkWithAuthorityAsync(operation with { SessionEpoch = 1 }, s_now)).Status);
        Assert.Equal(IdentityLinkStatus.AuthorityChanged, (await repo.LinkWithAuthorityAsync(operation with { ConfirmedMfaId = Guid.NewGuid() }, s_now)).Status);
        await using AuthDbContext db = database.CreateDbContext();
        Assert.Empty(await db.ExternalIdentities.ToListAsync());
        Assert.Equal(0, (await db.Accounts.SingleAsync(a => a.Id == account.Id)).SessionEpoch);
    }

    [Fact]
    public async Task Refuse_a_different_account_for_a_linked_subject_without_advancing_its_epoch()
    {
        using var database = SqliteDatabase.Auth();
        var accounts = new AccountRepository(database);
        Account first = await accounts.CreateAsync(StoreAuthenticationModelShould.Account("FIRST"));
        Account second = await accounts.CreateAsync(StoreAuthenticationModelShould.Account("SECOND"));
        var repo = new ExternalIdentityRepository(database, new FixedClock(s_now));
        var operation = new IdentityLinkOperation(Guid.NewGuid(), first.Id, "steam", "76561198000000001", 0, 0, null) { ProofExpiresAt = s_now.AddMinutes(5) };
        await repo.LinkWithAuthorityAsync(operation, s_now);
        Assert.Equal(IdentityLinkStatus.SubjectTaken, (await repo.LinkWithAuthorityAsync(operation with { OperationId = Guid.NewGuid(), AccountId = second.Id }, s_now)).Status);
        Assert.Equal(0, (await accounts.FindByIdAsync(second.Id))!.SessionEpoch);
    }

    [Fact]
    public async Task Refuse_evidence_that_expires_before_the_database_write()
    {
        using var database = SqliteDatabase.Auth();
        var accounts = new AccountRepository(database);
        Account account = await accounts.CreateAsync(StoreAuthenticationModelShould.Account("EXPIRED"));
        var repo = new ExternalIdentityRepository(database, new FixedClock(s_now.AddMinutes(5)));
        var operation = new IdentityLinkOperation(Guid.NewGuid(), account.Id, "steam", "76561198000000001", 0, 0, null)
        { ProofExpiresAt = s_now.AddMinutes(5) };
        Assert.Equal(IdentityLinkStatus.AuthorityChanged, (await repo.LinkWithAuthorityAsync(operation, s_now)).Status);
        Assert.Null(await repo.FindAsync("steam", "76561198000000001"));
        Assert.Equal(0, (await accounts.FindByIdAsync(account.Id))!.SessionEpoch);
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
