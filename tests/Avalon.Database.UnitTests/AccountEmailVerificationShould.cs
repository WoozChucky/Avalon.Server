using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Database.UnitTests;

public sealed class AccountEmailVerificationShould
{
    private static readonly DateTime Now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string Digest = new('a', 64);
    private static readonly string Replacement = new('b', 64);

    [Fact]
    public async Task StoresDigestAndConsumesOnlyOnceWithoutChangingGameAuthority()
    {
        using var db = SqliteDatabase.Auth();
        var account = await new AccountRepository(db).CreateAsync(StoreAuthenticationModelShould.Account("VERIFY"));
        var repo = new AccountEmailVerificationRepository(db);
        Assert.Equal(EmailVerificationIssueResult.Issued, await Issue(repo, account));
        await using (var context = db.CreateDbContext())
        {
            var challenge = await context.AccountEmailVerifications.SingleAsync();
            Assert.Equal(Digest, challenge.TokenHash);
            Assert.Equal(Now.AddMinutes(30), challenge.ExpiresAt);
            Assert.Null((await context.Accounts.SingleAsync(a => a.Id == account.Id)).EmailVerifiedAt);
        }
        Assert.True(await repo.ConsumeAsync(account.Id, Digest, Now.AddMinutes(29), default));
        Assert.False(await repo.ConsumeAsync(account.Id, Digest, Now.AddMinutes(29), default));
        var stored = await new AccountRepository(db).FindByIdAsync(account.Id);
        Assert.Equal(Now.AddMinutes(29), stored!.EmailVerifiedAt);
        Assert.Equal(0, stored.CredentialsVersion);
        Assert.Equal(0, stored.SessionEpoch);
        Assert.Equal(EmailVerificationIssueResult.AlreadyVerified, await Issue(repo, account, Now.AddMinutes(31)));
    }

    [Theory]
    [InlineData("expiry")] [InlineData("email")] [InlineData("credentials")]
    [InlineData("account")] [InlineData("consolidation")] [InlineData("banned")]
    public async Task RefusesInvalidProofWithoutConsumingIt(string cause)
    {
        using var db = SqliteDatabase.Auth();
        var account = await new AccountRepository(db).CreateAsync(StoreAuthenticationModelShould.Account("INVALID"));
        var repo = new AccountEmailVerificationRepository(db);
        await Issue(repo, account);
        await using (var context = db.CreateDbContext())
        {
            var target = context.Accounts.Where(a => a.Id == account.Id);
            if (cause == "email") await target.ExecuteUpdateAsync(u => u.SetProperty(a => a.Email, "changed@example.test"));
            if (cause == "credentials") await target.ExecuteUpdateAsync(u => u.SetProperty(a => a.CredentialsVersion, 1));
            if (cause == "consolidation") await target.ExecuteUpdateAsync(u => u.SetProperty(a => a.GameplayConsolidationId, (Guid?)Guid.NewGuid()));
            if (cause == "banned") await target.ExecuteUpdateAsync(u => u.SetProperty(a => a.Status, AccountStatus.Banned));
        }
        var presentedAccount = cause == "account" ? new AccountId(account.Id.Value + 99) : account.Id;
        Assert.False(await repo.ConsumeAsync(presentedAccount, Digest, cause == "expiry" ? Now.AddMinutes(30) : Now.AddMinutes(1), default));
        await using var read = db.CreateDbContext();
        Assert.Null((await read.AccountEmailVerifications.SingleAsync()).ConsumedAt);
        Assert.Null((await read.Accounts.SingleAsync(a => a.Id == account.Id)).EmailVerifiedAt);
    }

    [Fact]
    public async Task ReplacementAndLateSendCleanupOnlyAffectTheMatchingDigest()
    {
        using var db = SqliteDatabase.Auth();
        var account = await new AccountRepository(db).CreateAsync(StoreAuthenticationModelShould.Account("REPLACE"));
        var repo = new AccountEmailVerificationRepository(db);
        await Issue(repo, account);
        Assert.Equal(EmailVerificationIssueResult.Cooldown, await Issue(repo, account, Now.AddSeconds(59), Replacement));
        Assert.Equal(EmailVerificationIssueResult.Issued, await Issue(repo, account, Now.AddSeconds(60), Replacement));
        Assert.False(await repo.InvalidateAsync(account.Id, Digest, default));
        Assert.False(await repo.ConsumeAsync(account.Id, Digest, Now.AddSeconds(61), default));
        Assert.True(await repo.ConsumeAsync(account.Id, Replacement, Now.AddSeconds(61), default));
    }

    [Fact]
    public async Task FailedSendInvalidationAndMismatchedIssuanceNeverVerify()
    {
        using var db = SqliteDatabase.Auth();
        var account = await new AccountRepository(db).CreateAsync(StoreAuthenticationModelShould.Account("FAIL"));
        var repo = new AccountEmailVerificationRepository(db);
        Assert.Equal(EmailVerificationIssueResult.AccountChanged, await repo.IssueAsync(account.Id, "wrong@example.test", 0, Digest, Now, Now.AddMinutes(30), TimeSpan.FromSeconds(60), default));
        await Issue(repo, account);
        Assert.True(await repo.InvalidateAsync(account.Id, Digest, default));
        Assert.False(await repo.ConsumeAsync(account.Id, Digest, Now.AddSeconds(1), default));
        Assert.Equal(EmailVerificationIssueResult.Cooldown, await Issue(repo, account, Now.AddSeconds(1), Replacement));
    }

    [Fact]
    public async Task ConcurrentIssueAndConsumeHaveOneWinner()
    {
        using var db = SqliteDatabase.Auth();
        var account = await new AccountRepository(db).CreateAsync(StoreAuthenticationModelShould.Account("RACE"));
        var repo = new AccountEmailVerificationRepository(db);
        var issues = await Task.WhenAll(Issue(repo, account), Issue(repo, account, hash: Replacement));
        Assert.Single(issues, x => x == EmailVerificationIssueResult.Issued);
        Assert.Single(issues, x => x == EmailVerificationIssueResult.Cooldown);
        await using var read = db.CreateDbContext();
        var digest = (await read.AccountEmailVerifications.SingleAsync()).TokenHash;
        var results = await Task.WhenAll(repo.ConsumeAsync(account.Id, digest, Now, default), repo.ConsumeAsync(account.Id, digest, Now, default));
        Assert.Single(results, x => x);
    }

    [Fact]
    public async Task OrdinaryEmailReplacementClearsVerification()
    {
        using var db = SqliteDatabase.Auth();
        var account = StoreAuthenticationModelShould.Account("CHANGE"); account.EmailVerifiedAt = Now;
        account = await new AccountRepository(db).CreateAsync(account);
        await using var context = db.CreateDbContext();
        Assert.Equal(1, await AccountRepository.SetEmailAsync(context, account.Id, "new@example.test", 0));
        Assert.Null((await new AccountRepository(db).FindByIdAsync(account.Id))!.EmailVerifiedAt);
    }

    private static Task<EmailVerificationIssueResult> Issue(AccountEmailVerificationRepository repo, Account account,
        DateTime? now = null, string? hash = null) => repo.IssueAsync(account.Id, account.Email!, account.CredentialsVersion,
            hash ?? Digest, now ?? Now, (now ?? Now).AddMinutes(30), TimeSpan.FromSeconds(60), default);
}
