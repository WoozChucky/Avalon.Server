using Avalon.Common.ValueObjects;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Database.UnitTests;

public sealed class AccountEmailVerificationShould
{
    private static readonly DateTime s_now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string s_digest = new('a', 64);
    private static readonly string s_replacement = new('b', 64);

    [Fact]
    public async Task StoresDigestAndConsumesOnlyOnceWithoutChangingGameAuthority()
    {
        using var db = SqliteDatabase.Auth();
        Account account = await new AccountRepository(db).CreateAsync(StoreAuthenticationModelShould.Account("VERIFY"));
        var repo = new AccountEmailVerificationRepository(db);
        Assert.Equal(EmailVerificationIssueResult.Issued, await Issue(repo, account));
        await using (AuthDbContext context = db.CreateDbContext())
        {
            AccountEmailVerification challenge = await context.AccountEmailVerifications.SingleAsync();
            Assert.Equal(s_digest, challenge.TokenHash);
            Assert.Equal(s_now.AddMinutes(30), challenge.ExpiresAt);
            Assert.Null((await context.Accounts.SingleAsync(a => a.Id == account.Id)).EmailVerifiedAt);
        }
        Assert.True(await repo.ConsumeAsync(account.Id, s_digest, s_now.AddMinutes(29), default));
        Assert.False(await repo.ConsumeAsync(account.Id, s_digest, s_now.AddMinutes(29), default));
        Account? stored = await new AccountRepository(db).FindByIdAsync(account.Id);
        Assert.Equal(s_now.AddMinutes(29), stored!.EmailVerifiedAt);
        Assert.Equal(0, stored.CredentialsVersion);
        Assert.Equal(0, stored.SessionEpoch);
        Assert.Equal(EmailVerificationIssueResult.AlreadyVerified, await Issue(repo, account, s_now.AddMinutes(31)));
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("email")]
    [InlineData("credentials")]
    [InlineData("account")]
    [InlineData("consolidation")]
    [InlineData("banned")]
    public async Task RefusesInvalidProofWithoutConsumingIt(string cause)
    {
        using var db = SqliteDatabase.Auth();
        Account account = await new AccountRepository(db).CreateAsync(StoreAuthenticationModelShould.Account("INVALID"));
        var repo = new AccountEmailVerificationRepository(db);
        await Issue(repo, account);
        await using (AuthDbContext context = db.CreateDbContext())
        {
            IQueryable<Account> target = context.Accounts.Where(a => a.Id == account.Id);
            if (cause == "email") await target.ExecuteUpdateAsync(u => u.SetProperty(a => a.Email, "changed@example.test"));
            if (cause == "credentials") await target.ExecuteUpdateAsync(u => u.SetProperty(a => a.CredentialsVersion, 1));
            if (cause == "consolidation") await target.ExecuteUpdateAsync(u => u.SetProperty(a => a.GameplayConsolidationId, (Guid?)Guid.NewGuid()));
            if (cause == "banned") await target.ExecuteUpdateAsync(u => u.SetProperty(a => a.Status, AccountStatus.Banned));
        }
        AccountId presentedAccount = cause == "account" ? new AccountId(account.Id.Value + 99) : account.Id;
        Assert.False(await repo.ConsumeAsync(presentedAccount, s_digest, cause == "expiry" ? s_now.AddMinutes(30) : s_now.AddMinutes(1), default));
        await using AuthDbContext read = db.CreateDbContext();
        Assert.Null((await read.AccountEmailVerifications.SingleAsync()).ConsumedAt);
        Assert.Null((await read.Accounts.SingleAsync(a => a.Id == account.Id)).EmailVerifiedAt);
    }

    [Fact]
    public async Task ReplacementAndLateSendCleanupOnlyAffectTheMatchingDigest()
    {
        using var db = SqliteDatabase.Auth();
        Account account = await new AccountRepository(db).CreateAsync(StoreAuthenticationModelShould.Account("REPLACE"));
        var repo = new AccountEmailVerificationRepository(db);
        await Issue(repo, account);
        Assert.Equal(EmailVerificationIssueResult.Cooldown, await Issue(repo, account, s_now.AddSeconds(59), s_replacement));
        Assert.Equal(EmailVerificationIssueResult.Issued, await Issue(repo, account, s_now.AddSeconds(60), s_replacement));
        Assert.False(await repo.InvalidateAsync(account.Id, s_digest, default));
        Assert.False(await repo.ConsumeAsync(account.Id, s_digest, s_now.AddSeconds(61), default));
        Assert.True(await repo.ConsumeAsync(account.Id, s_replacement, s_now.AddSeconds(61), default));
    }

    [Fact]
    public async Task FailedSendInvalidationAndMismatchedIssuanceNeverVerify()
    {
        using var db = SqliteDatabase.Auth();
        Account account = await new AccountRepository(db).CreateAsync(StoreAuthenticationModelShould.Account("FAIL"));
        var repo = new AccountEmailVerificationRepository(db);
        Assert.Equal(EmailVerificationIssueResult.AccountChanged, await repo.IssueAsync(account.Id, "wrong@example.test", 0, s_digest, s_now, s_now.AddMinutes(30), TimeSpan.FromSeconds(60), default));
        await Issue(repo, account);
        Assert.True(await repo.InvalidateAsync(account.Id, s_digest, default));
        Assert.False(await repo.ConsumeAsync(account.Id, s_digest, s_now.AddSeconds(1), default));
        Assert.Equal(EmailVerificationIssueResult.Cooldown, await Issue(repo, account, s_now.AddSeconds(1), s_replacement));
    }

    [Fact]
    public async Task ConcurrentIssueAndConsumeHaveOneWinner()
    {
        using var db = SqliteDatabase.Auth();
        Account account = await new AccountRepository(db).CreateAsync(StoreAuthenticationModelShould.Account("RACE"));
        var repo = new AccountEmailVerificationRepository(db);
        EmailVerificationIssueResult[] issues = await Task.WhenAll(Issue(repo, account), Issue(repo, account, hash: s_replacement));
        Assert.Single(issues, x => x == EmailVerificationIssueResult.Issued);
        Assert.Single(issues, x => x == EmailVerificationIssueResult.Cooldown);
        await using AuthDbContext read = db.CreateDbContext();
        string digest = (await read.AccountEmailVerifications.SingleAsync()).TokenHash;
        bool[] results = await Task.WhenAll(repo.ConsumeAsync(account.Id, digest, s_now, default), repo.ConsumeAsync(account.Id, digest, s_now, default));
        Assert.Single(results, x => x);
    }

    private static Task<EmailVerificationIssueResult> Issue(AccountEmailVerificationRepository repo, Account account,
        DateTime? now = null, string? hash = null) => repo.IssueAsync(account.Id, account.Email!, account.CredentialsVersion,
            hash ?? s_digest, now ?? s_now, (now ?? s_now).AddMinutes(30), TimeSpan.FromSeconds(60), default);
}
