using Avalon.Api.Commerce;
using Avalon.Common.GameAuth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Avalon.Api.UnitTests.Commerce;

public sealed class PaymentReconciliationShould
{
    [Fact]
    public async Task Paid_without_return_page_is_fulfilled_and_duplicate_success_grants_once()
    {
        using var f = await Setup();
        Assert.True((await Process(f)).Completed);
        Assert.True((await Process(f)).Completed);
        await using var db = f.Db.CreateDbContext();
        var grant = Assert.Single(await db.GameLicenses.ToListAsync());
        var order = await db.PurchaseOrders.SingleAsync();
        Assert.Equal($"purchase:{order.Id:N}", grant.LicenseReference);
        Assert.Equal("avalon", grant.Provider);
        Assert.Equal("development", grant.Environment);
        Assert.Equal(f.Account.Id, grant.AccountId);
        Assert.Equal(LicenseAuthorityKind.StoredGrant, grant.AuthorityKind);
        Assert.False(grant.Authorizes(f.Account.Id, order.Product, "production", f.Clock.Now));
        Assert.Equal(grant.Id, order.LicenseId);
    }

    [Theory]
    [InlineData("unpaid")]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("merchant")]
    [InlineData("environment")]
    [InlineData("price")]
    [InlineData("catalog")]
    [InlineData("quantity")]
    [InlineData("tax")]
    [InlineData("attempt")]
    public async Task Unpaid_or_wrong_binding_never_grants(string mismatch)
    {
        using var f = await Setup();
        var s = f.Provider.Snapshot!;
        f.Provider.Snapshot = mismatch switch
        {
            "unpaid" => s with { Paid = false, State = PaymentAttemptState.Processing },
            "amount" => s with { AmountMinor = s.AmountMinor + 1 },
            "currency" => s with { Currency = "usd" },
            "merchant" => s with { ProviderAccountId = "other" },
            "environment" => s with { PaymentEnvironment = "other" },
            "price" => s with { PriceReference = "other" },
            "catalog" => s with { CatalogProductReference = "other" },
            "quantity" => s with { Quantity = 2 },
            "tax" => s with { TaxComplete = false },
            _ => s with { AttemptId = Guid.NewGuid() },
        };
        await Process(f);
        await using var db = f.Db.CreateDbContext();
        Assert.Empty(await db.GameLicenses.ToListAsync());
    }

    [Fact]
    public async Task Refund_before_success_cannot_grant()
    {
        using var f = await Setup();
        f.Provider.Snapshot = f.Provider.Snapshot! with { Refunds = [Refund(PaymentRefundState.Succeeded)] };
        await Process(f);
        f.Provider.Snapshot = f.Provider.Snapshot with { Refunds = [] };
        await Process(f);
        await using var db = f.Db.CreateDbContext();
        Assert.Empty(await db.GameLicenses.ToListAsync());
        Assert.NotNull((await db.PurchaseOrders.SingleAsync()).ReversedAt);
    }

    [Fact]
    public async Task Dispute_before_success_creates_suspended_grant()
    {
        using var f = await Setup();
        f.Provider.Snapshot = f.Provider.Snapshot! with { Disputes = [Dispute("one", PaymentDisputeState.Open)] };
        await Process(f);
        await using var db = f.Db.CreateDbContext();
        var grant = await db.GameLicenses.SingleAsync();
        Assert.NotNull(grant.SuspendedAt);
        var hold = await db.LicenseHolds.SingleAsync();
        Assert.Equal((await db.PaymentDisputes.SingleAsync()).Id.ToString("N"), hold.CauseReference);
    }

    [Theory]
    [InlineData(PaymentRefundState.Pending, false)]
    [InlineData(PaymentRefundState.Failed, false)]
    [InlineData(PaymentRefundState.Succeeded, true)]
    public async Task Only_confirmed_full_refund_revokes(PaymentRefundState state, bool revoked)
    {
        using var f = await Setup();
        await Process(f);
        f.Provider.Snapshot = f.Provider.Snapshot! with { Refunds = [Refund(state)] };
        await Process(f);
        await using var db = f.Db.CreateDbContext();
        Assert.Equal(revoked, (await db.GameLicenses.SingleAsync()).RevokedAt is not null);
    }

    [Fact]
    public async Task Failed_refund_after_success_never_restores_license()
    {
        using var f = await Setup();
        await Process(f);
        f.Provider.Snapshot = f.Provider.Snapshot! with { Refunds = [Refund(PaymentRefundState.Succeeded)] };
        await Process(f);
        f.Provider.Snapshot = f.Provider.Snapshot with { Refunds = [Refund(PaymentRefundState.Failed)] };
        await Process(f);
        await using var db = f.Db.CreateDbContext();
        var grant = await db.GameLicenses.SingleAsync();
        Assert.NotNull(grant.RevokedAt);
        Assert.Equal(2, grant.AuthorityRevision);
        Assert.Equal(PaymentRefundState.Succeeded, (await db.PaymentRefunds.SingleAsync()).State);
    }

    [Theory]
    [InlineData(PaymentDisputeState.Open, true, false)]
    [InlineData(PaymentDisputeState.UnderReview, true, false)]
    [InlineData(PaymentDisputeState.Won, false, false)]
    [InlineData(PaymentDisputeState.Lost, false, true)]
    [InlineData(PaymentDisputeState.Accepted, false, true)]
    [InlineData(PaymentDisputeState.Inquiry, false, false)]
    public async Task Formal_disputes_suspend_or_revoke_and_inquiry_is_review_only(PaymentDisputeState state, bool held, bool revoked)
    {
        using var f = await Setup();
        await Process(f);
        f.Provider.Snapshot = f.Provider.Snapshot! with { Disputes = [Dispute("one", state)] };
        await Process(f);
        await using var db = f.Db.CreateDbContext();
        var grant = await db.GameLicenses.SingleAsync();
        Assert.Equal(held, grant.SuspendedAt is not null);
        Assert.Equal(revoked, grant.RevokedAt is not null);
        if (state == PaymentDisputeState.Inquiry) Assert.NotNull((await db.PurchaseOrders.SingleAsync()).ReconciliationIssue);
    }

    [Fact]
    public async Task Win_releases_matching_hold_only_and_never_reverses_terminal_revocation()
    {
        using var f = await Setup();
        f.Provider.Snapshot = f.Provider.Snapshot! with { Disputes = [Dispute("one", PaymentDisputeState.Open), Dispute("two", PaymentDisputeState.Open)] };
        await Process(f);
        f.Provider.Snapshot = f.Provider.Snapshot with { Disputes = [Dispute("one", PaymentDisputeState.Won), Dispute("two", PaymentDisputeState.Open)] };
        await Process(f);
        await using (var db = f.Db.CreateDbContext()) Assert.NotNull((await db.GameLicenses.SingleAsync()).SuspendedAt);
        f.Provider.Snapshot = f.Provider.Snapshot with { Disputes = [Dispute("one", PaymentDisputeState.Won), Dispute("two", PaymentDisputeState.Lost)] };
        await Process(f);
        f.Provider.Snapshot = f.Provider.Snapshot with { Disputes = [Dispute("one", PaymentDisputeState.Won), Dispute("two", PaymentDisputeState.Won)] };
        await Process(f);
        await using var read = f.Db.CreateDbContext();
        Assert.NotNull((await read.GameLicenses.SingleAsync()).RevokedAt);
    }

    [Fact]
    public async Task Unsupported_partial_refund_requires_review_without_partial_refund_operations()
    {
        using var f = await Setup();
        await Process(f);
        f.Provider.Snapshot = f.Provider.Snapshot! with { Refunds = [Refund(PaymentRefundState.Succeeded) with { AmountMinor = 400 }] };
        Assert.True((await Process(f)).NeedsReview);
        await using var db = f.Db.CreateDbContext();
        Assert.NotNull((await db.PurchaseOrders.SingleAsync()).ReconciliationIssue);
        Assert.Null((await db.GameLicenses.SingleAsync()).RevokedAt);
    }

    internal static async Task<PurchaseServiceShould.Fixture> Setup()
    {
        var f = await PurchaseServiceShould.Fixture.Create();
        var result = await f.Service.CreateCheckoutAsync(f.Account.Id, 0, "test-source");
        var attempt = (await new PurchaseRepository(f.Db, f.Clock).FindLatestAttemptAsync(result.OrderId))!;
        f.Provider.Snapshot = new("alternate", "merchant", "sandbox", result.OrderId, attempt.Id, "checkout", "payment", "price", "catalog", 1,
            800, "eur", 150, 650, true, true, PaymentAttemptState.Paid, f.Clock.Now.AddMinutes(30), [], []);
        return f;
    }

    [Fact]
    public async Task Duplicate_payment_requires_review_not_second_grant_and_its_refund_leaves_funding_grant()
    {
        using var f = await Setup();
        await Process(f);
        var original = f.Provider.Snapshot!;
        var duplicateId = Guid.NewGuid();
        await using (var db = f.Db.CreateDbContext())
        {
            db.PaymentAttempts.Add(new PaymentAttempt { Id = duplicateId, OrderId = original.OrderId, Sequence = 2, Provider = "alternate", ProviderAccountId = "merchant",
                Environment = "sandbox", OperationKey = "second-operation", ProviderPriceId = "price", ProviderCatalogProductId = "catalog", PaymentMethods = "card",
                CheckoutEmail = "buyer@example.test", SuccessUrl = "https://avalon.example.test", CancelUrl = "https://avalon.example.test",
                State = PaymentAttemptState.CheckoutOpen, CheckoutReference = "second-checkout", RequestedExpiresAt = f.Clock.Now.AddMinutes(30), FirstDispatchedAt = f.Clock.Now, CreatedAt = f.Clock.Now });
            await db.SaveChangesAsync();
        }
        f.Provider.Snapshot = original with { AttemptId = duplicateId, CheckoutReference = "second-checkout", PaymentReference = "second-payment" };
        Assert.True((await Process(f)).NeedsReview);
        f.Provider.Snapshot = f.Provider.Snapshot with { Refunds = [Refund(PaymentRefundState.Succeeded) with { RefundReference = "second-refund", PaymentReference = "second-payment" }] };
        await Process(f);
        await using var read = f.Db.CreateDbContext();
        var grant = Assert.Single(await read.GameLicenses.ToListAsync());
        Assert.Null(grant.RevokedAt);
        Assert.Equal(original.AttemptId, (await read.PurchaseOrders.SingleAsync()).FundingAttemptId);
    }

    [Fact]
    public async Task Expired_worker_lease_cannot_overwrite_newer_negative_state()
    {
        using var f = await Setup();
        await Process(f);
        var repo = new PurchaseRepository(f.Db, f.Clock);
        var stale = (await repo.ClaimAttemptAsync(f.Provider.Snapshot!.AttemptId, f.Clock.Now, TimeSpan.FromMinutes(2)))!;
        var before = (await repo.FindOrderAsync(f.Provider.Snapshot.OrderId))!;
        f.Clock.Now = f.Clock.Now.AddMinutes(3);
        f.Provider.Snapshot = f.Provider.Snapshot with { Refunds = [Refund(PaymentRefundState.Succeeded)] };
        await Process(f);
        var result = await repo.ApplySnapshotAsync(new(stale, before.Version, f.Provider.Snapshot with { Refunds = [] }));
        Assert.False(result.Applied);
        await using var db = f.Db.CreateDbContext();
        Assert.NotNull((await db.GameLicenses.SingleAsync()).RevokedAt);
    }

    [Fact]
    public async Task Consolidation_during_fulfillment_uses_trusted_beneficiary()
    {
        using var f = await Setup();
        var target = await new AccountRepository(f.Db).CreateAsync(new Avalon.Domain.Auth.Account { Username = "TARGET", Email = "target@example.test",
            Salt = [1], Verifier = [2], JoinDate = f.Clock.Now });
        await using (var db = f.Db.CreateDbContext())
        {
            await db.PurchaseOrders.Where(x => x.Id == f.Provider.Snapshot!.OrderId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.AccountId, target.Id).SetProperty(x => x.Version, x => x.Version + 1));
        }
        await Process(f);
        await using var read = f.Db.CreateDbContext();
        Assert.Equal(target.Id, (await read.GameLicenses.SingleAsync()).AccountId);
        Assert.Equal(f.Account.Id, (await read.PurchaseOrders.SingleAsync()).OriginalPurchaserAccountId);
    }

    [Fact]
    public async Task Missed_notification_is_recovered_by_periodic_sweep()
    {
        using var f = await Setup();
        var repo = new PurchaseRepository(f.Db, f.Clock);
        var worker = new PaymentReconciliationWorker(repo, new PaymentReconciliationService(repo, new PaymentProviderRegistry([f.Provider]), Options.Create(f.Config), f.Clock),
            Options.Create(f.Config), f.Clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<PaymentReconciliationWorker>.Instance);
        await worker.RunOnceAsync(CancellationToken.None);
        await using var db = f.Db.CreateDbContext();
        Assert.Single(await db.GameLicenses.ToListAsync());
    }

    internal static async Task<PaymentProcessingResult> Process(PurchaseServiceShould.Fixture f)
    {
        var repo = new PurchaseRepository(f.Db, f.Clock);
        var snapshot = f.Provider.Snapshot!;
        var row = new PaymentEvent { Id = Guid.NewGuid(), Provider = "alternate", ProviderAccountId = "merchant", Environment = "sandbox",
            ExternalReference = Guid.NewGuid().ToString("N"), Type = "paid", ResourceKind = "checkout", ResourceReference = snapshot.CheckoutReference,
            PaymentReference = snapshot.PaymentReference, OrderId = snapshot.OrderId, PaymentAttemptId = (await repo.FindLatestAttemptAsync(snapshot.OrderId))!.Id,
            CreatedAt = f.Clock.Now, NextAttemptAt = f.Clock.Now };
        await repo.AcceptEventAsync(row);
        var claim = (await repo.ClaimEventsAsync(f.Clock.Now, 10, TimeSpan.FromMinutes(2))).Single(x => x.Event.Id == row.Id);
        var service = new PaymentReconciliationService(repo, new PaymentProviderRegistry([f.Provider]), Options.Create(f.Config), f.Clock);
        var result = await service.ProcessAsync(claim, CancellationToken.None);
        await repo.CompleteEventAsync(row.Id, claim.LeaseId, result);
        return result;
    }
    private static RefundProviderResult Refund(PaymentRefundState state) => new("refund", "payment", 800, "eur", state);
    private static PaymentDisputeSnapshot Dispute(string id, PaymentDisputeState state) => new(id, "payment", 800, "eur", state);
}
