using System.Security.Cryptography;
using System.Text;
using Avalon.Api.Hosting.Exceptions;
using Avalon.Api.Identity.Config;
using Avalon.Api.Identity.Exceptions;
using Avalon.Api.Identity.Services.Email;
using Avalon.Api.Testing;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.Services;

public sealed class AccountEmailVerificationServiceShould
{
    [Fact]
    public async Task IssuesDigestOnlyAndVerifiesCurrentAddressWithoutCreatingLicense()
    {
        using Fixture fixture = await Fixture.Create();
        await fixture.Service.RequestAsync(fixture.Account.Id, "127.0.0.1", default);
        string token = fixture.Mail.Token;
        Assert.Equal(43, token.Length);
        Assert.Contains("https://avalon.nunolevezinho.xyz/account/email/verify#token=", fixture.Mail.Body);
        await using AuthDbContext db = fixture.Db.CreateDbContext();
        AccountEmailVerification challenge = await db.AccountEmailVerifications.SingleAsync();
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token))), challenge.TokenHash);
        Assert.NotEqual(token, challenge.TokenHash);
        Assert.Equal(fixture.Now.AddMinutes(30), challenge.ExpiresAt);
        await fixture.Service.ConfirmAsync(fixture.Account.Id, token, default);
        Assert.Equal(fixture.Now, (await fixture.Service.GetStatusAsync(fixture.Account.Id, default)).EmailVerifiedAt);
        Assert.Empty(await db.GameLicenses.ToListAsync());
        Assert.Equal(0, (await fixture.Accounts.FindByIdAsync(fixture.Account.Id))!.CredentialsVersion);
        await Assert.ThrowsAsync<BusinessException>(() => fixture.Service.ConfirmAsync(fixture.Account.Id, token, default));
    }

    [Theory]
    [InlineData("account")]
    [InlineData("email")]
    [InlineData("credentials")]
    [InlineData("expired")]
    [InlineData("replacement")]
    [InlineData("consolidation")]
    public async Task RefusesStaleOrCrossAccountProof(string condition)
    {
        using Fixture f = await Fixture.Create();
        await f.Service.RequestAsync(f.Account.Id, "127.0.0.1", default);
        string token = f.Mail.Token;
        AccountId accountId = f.Account.Id;
        if (condition == "account")
        {
            Account other = await f.Accounts.CreateAsync(new Account { Username = "OTHER", Email = "other@example.test", Salt = [1], Verifier = [2], JoinDate = f.Now });
            accountId = other.Id;
        }
        await using AuthDbContext db = f.Db.CreateDbContext();
        IQueryable<Account> target = db.Accounts.Where(a => a.Id == f.Account.Id);
        if (condition == "email") await target.ExecuteUpdateAsync(u => u.SetProperty(a => a.Email, "changed@example.test"));
        if (condition == "credentials") await target.ExecuteUpdateAsync(u => u.SetProperty(a => a.CredentialsVersion, 1));
        if (condition == "consolidation") await target.ExecuteUpdateAsync(u => u.SetProperty(a => a.GameplayConsolidationId, (Guid?)Guid.NewGuid()));
        if (condition == "expired") f.Clock.Advance(TimeSpan.FromMinutes(30));
        if (condition == "replacement")
        {
            f.Clock.Advance(TimeSpan.FromSeconds(60));
            await f.Service.RequestAsync(f.Account.Id, "127.0.0.1", default);
        }
        await Assert.ThrowsAsync<BusinessException>(() => f.Service.ConfirmAsync(accountId, token, default));
        Assert.Null((await f.Accounts.FindByIdAsync(f.Account.Id))!.EmailVerifiedAt);
        if (condition == "account") await f.Service.ConfirmAsync(f.Account.Id, token, default);
    }

    [Fact]
    public async Task FailedOrCanceledSendInvalidatesOnlyItsDigestAndAllowsBoundedResend()
    {
        using Fixture f = await Fixture.Create();
        f.Mail.Failure = new OperationCanceledException("mail-token-in-sensitive-provider-error");
        EmailDeliveryException ex = await Assert.ThrowsAsync<EmailDeliveryException>(() => f.Service.RequestAsync(f.Account.Id, "127.0.0.1", default));
        Assert.DoesNotContain("mail-token", ex.ToString());
        Assert.Null((await f.Accounts.FindByIdAsync(f.Account.Id))!.EmailVerifiedAt);
        await Assert.ThrowsAsync<BusinessException>(() => f.Service.ConfirmAsync(f.Account.Id, f.Mail.Token, default));
        await Assert.ThrowsAsync<AccountLockedException>(() => f.Service.RequestAsync(f.Account.Id, "127.0.0.1", default));
        f.Mail.Failure = null; f.Clock.Advance(TimeSpan.FromSeconds(60));
        await f.Service.RequestAsync(f.Account.Id, "127.0.0.1", default);
        await f.Service.ConfirmAsync(f.Account.Id, f.Mail.Token, default);
    }

    [Fact]
    public async Task LateFailedSendDoesNotInvalidateAReplacement()
    {
        using Fixture f = await Fixture.Create();
        string? replacement = null;
        f.Mail.BeforeFailure = async () =>
        {
            f.Mail.BeforeFailure = null; f.Clock.Advance(TimeSpan.FromSeconds(60));
            await f.Service.RequestAsync(f.Account.Id, "127.0.0.1", default);
            replacement = f.Mail.Token;
        };
        await Assert.ThrowsAsync<EmailDeliveryException>(() => f.Service.RequestAsync(f.Account.Id, "127.0.0.1", default));
        await f.Service.ConfirmAsync(f.Account.Id, replacement!, default);
    }

    [Fact]
    public async Task SourceBudgetNormalizesIpv6AndAccountBudgetBoundsFailedAttempts()
    {
        using Fixture f = await Fixture.Create();
        f.Config.MaxVerificationSendsPerSource = 1;
        await f.Service.RequestAsync(f.Account.Id, "2001:db8::1", default);
        f.Clock.Advance(TimeSpan.FromSeconds(60));
        await Assert.ThrowsAsync<AccountLockedException>(() => f.Service.RequestAsync(f.Account.Id, "2001:db8::2", default));
        f.Config.MaxVerificationSendsPerAccount = 2;
        await Assert.ThrowsAsync<AccountLockedException>(() => f.Service.RequestAsync(f.Account.Id, "192.0.2.4", default));
    }

    [Fact]
    public async Task SenderUnavailableAndAccountsWithoutEmailStayUnverified()
    {
        using Fixture f = await Fixture.Create();
        AccountEmailVerificationService service = f.MakeService(null);
        Assert.False((await service.GetStatusAsync(f.Account.Id, default)).DeliveryAvailable);
        await Assert.ThrowsAsync<EmailVerificationUnavailableException>(() => service.RequestAsync(f.Account.Id, "127.0.0.1", default));
        await using AuthDbContext db = f.Db.CreateDbContext();
        await db.Accounts.Where(a => a.Id == f.Account.Id).ExecuteUpdateAsync(u => u.SetProperty(a => a.Email, (string?)null));
        await Assert.ThrowsAsync<BusinessException>(() => f.Service.RequestAsync(f.Account.Id, "127.0.0.1", default));
    }

    internal sealed class Fixture : IDisposable
    {
        public SqliteAuthDatabase Db { get; } = new();
        public AccountRepository Accounts { get; }
        public Account Account = null!;
        public readonly Clock Clock = new();
        public DateTime Now => Clock.GetUtcNow().UtcDateTime;
        public readonly Mail Mail = new();
        public readonly EmailConfig Config = new() { VerificationSiteOrigin = "https://avalon.nunolevezinho.xyz" };
        public readonly IReplicatedCache Cache = Substitute.For<IReplicatedCache>();
        public AccountEmailVerificationService Service => MakeService(Mail);
        private Fixture()
        {
            Accounts = new AccountRepository(Db);
            var counts = new Dictionary<string, long>();
            Cache.IncrementAsync(Arg.Any<string>(), Arg.Any<TimeSpan>()).Returns(call =>
            {
                string key = call.ArgAt<string>(0);
                counts[key] = counts.GetValueOrDefault(key) + 1;
                return counts[key];
            });
        }
        public static async Task<Fixture> Create()
        {
            var f = new Fixture();
            f.Account = await f.Accounts.CreateAsync(new Account { Username = "VERIFY", Email = "verify@example.test", Salt = [1], Verifier = [2], JoinDate = f.Now });
            return f;
        }
        public AccountEmailVerificationService MakeService(IEmailSender? sender) => new(Accounts,
            new AccountEmailVerificationRepository(Db), Cache, Config, Clock, sender);
        public void Dispose() => Db.Dispose();
    }

    internal sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan time) => _now += time;
    }
    internal sealed class Mail : IEmailSender
    {
        public string Body = "";
        public Exception? Failure;
        public Func<Task>? BeforeFailure;
        public string Token => Body[(Body.IndexOf("#token=", StringComparison.Ordinal) + 7)..].Split('\n')[0].Trim();
        public async Task SendAsync(string to, string subject, string textBody, CancellationToken ct)
        {
            Body = textBody;
            if (BeforeFailure is { } action) { await action(); throw new IOException("sensitive-mail-token"); }
            if (Failure is not null) throw Failure;
        }
    }
}
