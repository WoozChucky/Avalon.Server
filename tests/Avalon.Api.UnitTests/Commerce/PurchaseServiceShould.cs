using Avalon.Api.Commerce;
using Avalon.Api.UnitTests.Services;
using Avalon.Common.Accounts;
using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Domain.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Avalon.Api.UnitTests.Commerce;

public sealed class PurchaseServiceShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unverified_or_inactive_account_never_calls_provider(bool inactive)
    {
        using var f = await Fixture.Create();
        await using (var db = f.Db.CreateDbContext())
        {
            var account = await db.Accounts.SingleAsync(x => x.Id == f.Account.Id);
            if (inactive) account.Status = AccountStatus.Banned; else account.EmailVerifiedAt = null;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<PurchaseException>(() => f.Service.CreateCheckoutAsync(f.Account.Id, 0, "test-source"));
        Assert.Empty(f.Provider.Commands);
    }

    [Fact]
    public async Task Two_checkout_requests_share_one_order_and_operation()
    {
        using var f = await Fixture.Create();
        var first = await f.Service.CreateCheckoutAsync(f.Account.Id, 0, "test-source");
        var second = await f.Service.CreateCheckoutAsync(f.Account.Id, 0, "test-source");
        Assert.Equal(first.OrderId, second.OrderId);
        Assert.Equal(first.CheckoutUrl, second.CheckoutUrl);
        Assert.Single(f.Provider.Commands);
        Assert.Equal(1, f.Budget.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Active_or_suspended_avalon_license_blocks_repurchase(bool suspended)
    {
        using var f = await Fixture.Create();
        var grant = await f.Grant("avalon", LicenseAuthorityKind.StoredGrant);
        if (suspended) await new LicenseHoldRepository(f.Db).SetAsync(grant.Id, "support", "test", true, f.Clock.Now);
        var error = await Assert.ThrowsAsync<PurchaseException>(() => f.Service.CreateCheckoutAsync(f.Account.Id, 0, "test-source"));
        Assert.Equal(PurchaseErrors.LicenseAlreadyOwned, error.Code);
        Assert.Empty(f.Provider.Commands);
        Assert.Equal(suspended ? "Suspended" : "Active", (await f.Service.GetLicenseStatusAsync(f.Account.Id)).State);
    }

    [Fact]
    public async Task Steam_link_is_guidance_not_avalon_grant()
    {
        using var f = await Fixture.Create();
        await f.Grant("steam", LicenseAuthorityKind.VerifiedOwnership);
        var status = await f.Service.GetLicenseStatusAsync(f.Account.Id);
        Assert.Equal("None", status.State);
        Assert.True(status.HasStoreLicense);
        Assert.NotNull((await f.Service.CreateCheckoutAsync(f.Account.Id, 0, "test-source")).CheckoutUrl);
    }

    [Fact]
    public async Task Provider_timeout_keeps_same_operation_and_frozen_request()
    {
        using var f = await Fixture.Create();
        f.Provider.Unavailable = true;
        var first = await f.Service.CreateCheckoutAsync(f.Account.Id, 0, "test-source");
        Assert.Null(first.CheckoutUrl);
        f.Config.AmountMinor = 1200; f.Config.Currency = "usd"; f.Config.PublicSiteOrigin = "https://changed.example.test";
        f.Config.ProviderCatalogProductId = "changed"; f.Config.PaymentMethods = ["other"];
        f.Clock.Now = f.Clock.Now.AddMinutes(10);
        f.Provider.Unavailable = false;
        var second = await f.Service.CreateCheckoutAsync(f.Account.Id, 0, "test-source");
        Assert.Equal(first.OrderId, second.OrderId);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(f.Provider.Commands[0]), System.Text.Json.JsonSerializer.Serialize(f.Provider.Commands[1]));
        Assert.Equal(1, f.Budget.Calls);
    }

    [Fact]
    public async Task Checkout_unknown_after_replay_window_requires_review()
    {
        using var f = await Fixture.Create();
        f.Provider.Unavailable = true;
        await f.Service.CreateCheckoutAsync(f.Account.Id, 0, "test-source");
        f.Clock.Now = f.Clock.Now.AddHours(23);
        var error = await Assert.ThrowsAsync<PurchaseException>(() => f.Service.CreateCheckoutAsync(f.Account.Id, 0, "test-source"));
        Assert.Equal(PurchaseErrors.NeedsReview, error.Code);
        Assert.Single(f.Provider.Commands);
    }

    [Fact]
    public async Task Other_account_cannot_read_order()
    {
        using var f = await Fixture.Create();
        var created = await f.Service.CreateCheckoutAsync(f.Account.Id, 0, "test-source");
        var error = await Assert.ThrowsAsync<PurchaseException>(() => f.Service.GetOrderAsync(new AccountId(9999), created.OrderId));
        Assert.Equal(PurchaseErrors.NotFound, error.Code);
    }

    [Fact]
    public async Task Expired_local_url_does_not_authorize_another_operation()
    {
        using var f = await Fixture.Create();
        var first = await f.Service.CreateCheckoutAsync(f.Account.Id, 0, "test-source");
        f.Clock.Now = f.Clock.Now.AddHours(1);
        var retry = await f.Service.CreateCheckoutAsync(f.Account.Id, 0, "test-source");
        Assert.Equal(first.OrderId, retry.OrderId);
        Assert.Null(retry.CheckoutUrl);
        Assert.Single(f.Provider.Commands);
    }

    [Fact]
    public async Task Expired_never_dispatched_attempt_can_be_replaced_after_budget_refusal()
    {
        using var f = await Fixture.Create();
        f.Budget.Allowed = false;
        await Assert.ThrowsAsync<PurchaseException>(() => f.Service.CreateCheckoutAsync(f.Account.Id, 0, "test-source"));
        Assert.Empty(f.Provider.Commands);
        f.Clock.Now = f.Clock.Now.AddHours(1);
        f.Budget.Allowed = true;
        var reply = await f.Service.CreateCheckoutAsync(f.Account.Id, 0, "test-source");
        Assert.NotNull(reply.CheckoutUrl);
        Assert.True(Assert.Single(f.Provider.Commands).ExpiresAt > f.Clock.Now);
    }

    [Fact]
    public async Task Same_clock_closed_attempt_is_replaced_and_selected_reliably()
    {
        using var f = await Fixture.Create();
        f.Budget.Allowed = false;
        await Assert.ThrowsAsync<PurchaseException>(() => f.Service.CreateCheckoutAsync(f.Account.Id, 0, "test-source"));
        await using (var db = f.Db.CreateDbContext())
        {
            await db.PaymentAttempts.ExecuteUpdateAsync(u => u.SetProperty(x => x.Id, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"))
                .SetProperty(x => x.State, PaymentAttemptState.Expired));
        }
        f.Budget.Allowed = true;
        var reply = await f.Service.CreateCheckoutAsync(f.Account.Id, 0, "test-source");
        var latest = await new PurchaseRepository(f.Db, f.Clock).FindLatestAttemptAsync(reply.OrderId);
        Assert.Equal(Assert.Single(f.Provider.Commands).AttemptId, latest!.Id);
    }

    [Fact]
    public async Task Changing_merchant_after_timeout_never_replays_in_another_scope()
    {
        using var f = await Fixture.Create();
        f.Provider.Unavailable = true;
        await f.Service.CreateCheckoutAsync(f.Account.Id, 0, "test-source");
        f.Config.ProviderAccountId = "other-merchant";
        var error = await Assert.ThrowsAsync<PurchaseException>(() => f.Service.CreateCheckoutAsync(f.Account.Id, 0, "test-source"));
        Assert.Equal(PurchaseErrors.NeedsReview, error.Code);
        Assert.Single(f.Provider.Commands);
    }

    [Fact]
    public async Task Disabled_commerce_keeps_production_grants_visible()
    {
        using var f = await Fixture.Create();
        await new GameLicenseRepository(f.Db).RecordGrantAsync(new GameLicense
        {
            Id = Guid.NewGuid(),
            AccountId = f.Account.Id,
            Product = StoreAuthenticationConfiguration.Product,
            Provider = "avalon",
            Environment = "production",
            ProviderProductId = "base",
            AuthorityKind = LicenseAuthorityKind.StoredGrant,
            LicenseReference = "production-grant",
            GrantedAt = f.Clock.Now
        });
        f.Config.Enabled = false;
        var service = new PurchaseService(new PurchaseRepository(f.Db, f.Clock), new PaymentProviderRegistry([f.Provider]), Options.Create(f.Config),
            Options.Create(new StoreAuthenticationConfiguration { Environment = "production" }), f.Budget, f.Clock);
        Assert.Equal("Active", (await service.GetLicenseStatusAsync(f.Account.Id)).State);
        await Assert.ThrowsAsync<PurchaseException>(() => service.CreateCheckoutAsync(f.Account.Id, 0, "test-source"));
        Assert.Empty(f.Provider.Commands);
    }

    internal sealed class Fixture : IDisposable
    {
        public SqliteAuthDatabase Db { get; } = new();
        public Clock Clock { get; } = new();
        public FakeProvider Provider { get; } = new();
        public FakeBudget Budget { get; } = new();
        public CommerceConfiguration Config { get; } = new()
        {
            Enabled = true,
            Provider = "alternate",
            ProviderAccountId = "merchant",
            OfferId = "base",
            ProviderPriceId = "price",
            ProviderCatalogProductId = "catalog",
            PublicSiteOrigin = "https://avalon.example.test",
            PaymentMethods = ["card"]
        };
        public Account Account { get; private set; } = null!;
        public PurchaseService Service => new(new PurchaseRepository(Db, Clock), new PaymentProviderRegistry([Provider]), Options.Create(Config),
            Options.Create(new StoreAuthenticationConfiguration { Environment = "development" }), Budget, Clock);
        public static async Task<Fixture> Create()
        {
            var f = new Fixture();
            f.Account = await new AccountRepository(f.Db).CreateAsync(new Account { Username = "BUYER", Email = "buyer@example.test", Salt = [1], Verifier = [2], JoinDate = f.Clock.Now, EmailVerifiedAt = f.Clock.Now });
            return f;
        }
        public Task<GameLicense> Grant(string provider, LicenseAuthorityKind kind) => new GameLicenseRepository(Db).RecordGrantAsync(new GameLicense
        {
            Id = Guid.NewGuid(),
            AccountId = Account.Id,
            Product = StoreAuthenticationConfiguration.Product,
            Provider = provider,
            Environment = "development",
            ProviderProductId = "base",
            LicenseReference = Guid.NewGuid().ToString("N"),
            AuthorityKind = kind,
            GrantedAt = Clock.Now,
        });
        public void Dispose() => Db.Dispose();
    }
    internal sealed class Clock : TimeProvider
    {
        public DateTime Now { get; set; } = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
    internal sealed class FakeBudget : ICheckoutBudget
    {
        public int Calls;
        public bool Allowed = true;
        public Task<bool> TryTakeAsync(string environment, AccountId account, string source, Guid operation, CancellationToken ct)
        { Calls++; return Task.FromResult(Allowed); }
    }
    internal sealed class FakeProvider : IPaymentProvider
    {
        public string Provider => "alternate";
        public bool Unavailable;
        public PaymentSnapshot? Snapshot;
        public bool RefundUnavailable;
        public PaymentRefundState RefundState = PaymentRefundState.Pending;
        public List<FullRefundCommand> RefundCommands { get; } = [];
        public List<CheckoutCreateCommand> Commands { get; } = [];
        public Task<CheckoutProviderResult> CreateCheckoutAsync(CheckoutCreateCommand command, CancellationToken ct)
        {
            Commands.Add(command);
            if (Unavailable) throw new PaymentProviderException("PAYMENT_PROVIDER_UNAVAILABLE");
            return Task.FromResult(new CheckoutProviderResult("checkout", "https://pay.example.test/checkout", command.ExpiresAt));
        }
        public Task<PaymentSnapshot> GetCheckoutAsync(PaymentLookup lookup, CancellationToken ct) => Snapshot is { } value
            ? Task.FromResult(value) : throw new PaymentProviderException("PAYMENT_PROVIDER_UNAVAILABLE");
        public VerifiedPaymentNotification VerifyNotification(ReadOnlyMemory<byte> body, IReadOnlyDictionary<string, string> headers, DateTime now) => throw new NotSupportedException();
        public Task<RefundProviderResult> RequestFullRefundAsync(FullRefundCommand command, CancellationToken ct)
        {
            RefundCommands.Add(command);
            if (RefundUnavailable) throw new PaymentProviderException("PAYMENT_PROVIDER_UNAVAILABLE");
            return Task.FromResult(new RefundProviderResult("admin-refund", command.PaymentReference, command.AmountMinor, command.Currency, RefundState));
        }
    }
}
