using Avalon.Api.Contract.Commerce;
using Avalon.Common.Accounts;
using Avalon.Database;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Avalon.Api.Commerce.UnitTests;

public sealed class PurchaseAdministrationShould
{
    [Fact]
    public async Task Player_cannot_refund_or_retry()
    {
        using PurchaseServiceShould.Fixture f = await PaymentReconciliationShould.Setup();
        await PaymentReconciliationShould.Process(f);
        PurchaseAdministrationService service = Service(f);
        await Assert.ThrowsAsync<PurchaseException>(() => service.RequestFullRefundAsync(f.Account.Id, f.Provider.Snapshot!.OrderId, f.Provider.Snapshot.AttemptId, "requested"));
        await Assert.ThrowsAsync<PurchaseException>(() => service.RetryReconciliationAsync(f.Account.Id, f.Provider.Snapshot!.OrderId));
        Assert.Empty(f.Provider.RefundCommands);
    }

    [Fact]
    public async Task Refund_uses_recorded_payment_and_full_amount_and_repeated_clicks_share_operation()
    {
        using PurchaseServiceShould.Fixture f = await Ready();
        PurchaseAdministrationService service = Service(f);
        PaymentSnapshot snapshot = f.Provider.Snapshot!;
        RefundReply first = await service.RequestFullRefundAsync(f.Account.Id, snapshot.OrderId, snapshot.AttemptId, " Customer request ");
        RefundReply second = await service.RequestFullRefundAsync(f.Account.Id, snapshot.OrderId, snapshot.AttemptId, "different reason");
        Assert.Equal(first.RefundId, second.RefundId);
        FullRefundCommand command = Assert.Single(f.Provider.RefundCommands);
        Assert.Equal(snapshot.PaymentReference, command.PaymentReference);
        Assert.Equal(snapshot.AmountMinor, command.AmountMinor);
        Assert.Equal(snapshot.Currency, command.Currency);
        await using AuthDbContext db = f.Db.CreateDbContext();
        PaymentRefund operation = Assert.Single(await db.PaymentRefunds.ToListAsync());
        Assert.Equal(command.OperationKey, operation.OperationKey);
        Assert.Equal(f.Account.Id, operation.RequestedBy);
        Assert.Equal("Customer request", operation.Reason);
    }

    [Theory]
    [InlineData(PaymentRefundState.Pending)]
    [InlineData(PaymentRefundState.Failed)]
    [InlineData(PaymentRefundState.Canceled)]
    public async Task Pending_failed_or_canceled_refund_does_not_revoke(PaymentRefundState state)
    {
        using PurchaseServiceShould.Fixture f = await Ready();
        f.Provider.RefundState = state;
        await Service(f).RequestFullRefundAsync(f.Account.Id, f.Provider.Snapshot!.OrderId, f.Provider.Snapshot.AttemptId, "requested");
        await using AuthDbContext db = f.Db.CreateDbContext();
        Assert.Null((await db.GameLicenses.SingleAsync()).RevokedAt);
    }

    [Fact]
    public async Task Open_dispute_blocks_normal_refund()
    {
        using PurchaseServiceShould.Fixture f = await Ready();
        f.Provider.Snapshot = f.Provider.Snapshot! with { Disputes = [new("dispute", "payment", 800, "eur", PaymentDisputeState.Open)] };
        await Assert.ThrowsAsync<PurchaseException>(() => Service(f).RequestFullRefundAsync(f.Account.Id, f.Provider.Snapshot.OrderId, f.Provider.Snapshot.AttemptId, "requested"));
        Assert.Empty(f.Provider.RefundCommands);
    }

    [Fact]
    public async Task Unknown_refund_result_is_reconciled_without_duplicate()
    {
        using PurchaseServiceShould.Fixture f = await Ready();
        f.Provider.RefundUnavailable = true;
        PaymentSnapshot snapshot = f.Provider.Snapshot!;
        RefundReply first = await Service(f).RequestFullRefundAsync(f.Account.Id, snapshot.OrderId, snapshot.AttemptId, "requested");
        f.Provider.RefundUnavailable = false;
        RefundReply second = await Service(f).RequestFullRefundAsync(f.Account.Id, snapshot.OrderId, snapshot.AttemptId, "requested again");
        Assert.Equal(first.RefundId, second.RefundId);
        Assert.Equal(f.Provider.RefundCommands[0], f.Provider.RefundCommands[1]);
        await using AuthDbContext db = f.Db.CreateDbContext();
        Assert.Single(await db.PaymentRefunds.ToListAsync());
    }

    [Fact]
    public async Task Expired_unknown_refund_requires_review()
    {
        using PurchaseServiceShould.Fixture f = await Ready();
        f.Provider.RefundUnavailable = true;
        PaymentSnapshot snapshot = f.Provider.Snapshot!;
        await Service(f).RequestFullRefundAsync(f.Account.Id, snapshot.OrderId, snapshot.AttemptId, "requested");
        f.Clock.Now = f.Clock.Now.AddHours(23);
        PurchaseException error = await Assert.ThrowsAsync<PurchaseException>(() => Service(f).RequestFullRefundAsync(f.Account.Id, snapshot.OrderId, snapshot.AttemptId, "requested"));
        Assert.Equal(PurchaseErrors.NeedsReview, error.Code);
        Assert.Single(f.Provider.RefundCommands);
    }

    [Fact]
    public async Task Other_orders_attempt_cannot_be_refunded()
    {
        using PurchaseServiceShould.Fixture f = await Ready();
        await Assert.ThrowsAsync<PurchaseException>(() => Service(f).RequestFullRefundAsync(f.Account.Id, f.Provider.Snapshot!.OrderId, Guid.NewGuid(), "requested"));
        Assert.Empty(f.Provider.RefundCommands);
    }

    [Fact]
    public async Task Duplicate_payment_refund_does_not_revoke_original_license()
    {
        using PurchaseServiceShould.Fixture f = await Ready();
        PaymentSnapshot original = f.Provider.Snapshot!;
        var duplicate = Guid.NewGuid();
        await using (AuthDbContext db = f.Db.CreateDbContext())
        {
            db.PaymentAttempts.Add(new PaymentAttempt
            {
                Id = duplicate,
                OrderId = original.OrderId,
                Sequence = 2,
                Provider = "alternate",
                ProviderAccountId = "merchant",
                Environment = "sandbox",
                OperationKey = "duplicate",
                ProviderPriceId = "price",
                ProviderCatalogProductId = "catalog",
                PaymentMethods = "card",
                CheckoutEmail = "buyer@example.test",
                SuccessUrl = "https://avalon.example.test",
                CancelUrl = "https://avalon.example.test",
                State = PaymentAttemptState.Paid,
                CheckoutReference = "duplicate-checkout",
                PaymentReference = "duplicate-payment",
                CreatedAt = f.Clock.Now,
                FirstDispatchedAt = f.Clock.Now,
                RequestedExpiresAt = f.Clock.Now.AddMinutes(30)
            });
            await db.SaveChangesAsync();
        }
        f.Provider.Snapshot = original with { AttemptId = duplicate, CheckoutReference = "duplicate-checkout", PaymentReference = "duplicate-payment" };
        f.Provider.RefundState = PaymentRefundState.Succeeded;
        await Service(f).RequestFullRefundAsync(f.Account.Id, original.OrderId, duplicate, "Duplicate payment");
        await PaymentReconciliationShould.Process(f);
        await using AuthDbContext read = f.Db.CreateDbContext();
        Assert.Null((await read.GameLicenses.SingleAsync()).RevokedAt);
        Assert.Equal(original.AttemptId, (await read.PurchaseOrders.SingleAsync()).FundingAttemptId);
    }

    [Fact]
    public async Task Confirmed_refund_is_applied_by_reconciliation_not_administrator_acceptance()
    {
        using PurchaseServiceShould.Fixture f = await Ready();
        f.Provider.RefundState = PaymentRefundState.Succeeded;
        await Service(f).RequestFullRefundAsync(f.Account.Id, f.Provider.Snapshot!.OrderId, f.Provider.Snapshot.AttemptId, "requested");
        await using (AuthDbContext db = f.Db.CreateDbContext()) Assert.Null((await db.GameLicenses.SingleAsync()).RevokedAt);
        await PaymentReconciliationShould.Process(f);
        await using AuthDbContext read = f.Db.CreateDbContext();
        Assert.NotNull((await read.GameLicenses.SingleAsync()).RevokedAt);
    }

    [Fact]
    public async Task Bounded_search_and_detail_include_original_amount_audit_and_bound_license()
    {
        using PurchaseServiceShould.Fixture f = await Ready();
        PurchaseAdministrationService service = Service(f);
        PagedResult<AdminPurchaseDto> search = await service.SearchAsync(new Avalon.Api.Contract.Commerce.PurchaseSearch { PageSize = 10, AccountId = f.Account.Id.Value, State = "fulfilled", Environment = "sandbox" });
        AdminPurchaseDto listed = Assert.Single(search.Items);
        AdminPurchaseDetailDto detail = await service.GetAsync(listed.OrderId);
        Assert.Equal(f.Account.Id.Value, detail.Purchase.OriginalPurchaserAccountId);
        Assert.Equal(800, detail.Purchase.AmountMinor);
        Assert.Equal("BUYER", detail.BeneficiaryUsername);
        Assert.Equal("Active", detail.LicenseState);
        Assert.True(Assert.Single(detail.Attempts).Funding);
    }

    [Fact]
    public async Task Existing_external_reversal_does_not_create_a_new_outbound_refund_operation()
    {
        using PurchaseServiceShould.Fixture f = await Ready();
        f.Provider.Snapshot = f.Provider.Snapshot! with { Refunds = [new("external-refund", "payment", 200, "eur", PaymentRefundState.Pending)] };
        PurchaseException error = await Assert.ThrowsAsync<PurchaseException>(() => Service(f).RequestFullRefundAsync(f.Account.Id, f.Provider.Snapshot.OrderId, f.Provider.Snapshot.AttemptId, "requested"));
        Assert.Equal(PurchaseErrors.NeedsReview, error.Code);
        Assert.Empty(f.Provider.RefundCommands);
        await using AuthDbContext db = f.Db.CreateDbContext();
        Assert.Empty(await db.PaymentRefunds.ToListAsync());
    }

    [Fact]
    public async Task Retry_unknown_checkout_never_uses_an_internal_id_as_provider_reference()
    {
        using PurchaseServiceShould.Fixture f = await Ready();
        await using (AuthDbContext db = f.Db.CreateDbContext())
        {
            await db.PaymentAttempts.ExecuteUpdateAsync(u => u.SetProperty(x => x.CheckoutReference, (string?)null).SetProperty(x => x.PaymentReference, (string?)null)
                .SetProperty(x => x.State, PaymentAttemptState.ProviderUnknown));
        }
        await Service(f).RetryReconciliationAsync(f.Account.Id, f.Provider.Snapshot!.OrderId);
        var repo = new PurchaseRepository(f.Db, f.Clock);
        PaymentEventClaim claim = Assert.Single(await repo.ClaimEventsAsync(f.Clock.Now, 10, TimeSpan.FromMinutes(2)));
        await new PaymentReconciliationService(repo, new PaymentProviderRegistry([f.Provider]), Options.Create(f.Config), f.Clock).ProcessAsync(claim, CancellationToken.None);
        Assert.Equal(2, f.Provider.Commands.Count);
        Assert.Equal(f.Provider.Commands[0].OperationKey, f.Provider.Commands[1].OperationKey);
    }

    [Theory]
    [InlineData(PaymentRefundState.Failed)]
    [InlineData(PaymentRefundState.Canceled)]
    public async Task Known_unsuccessful_refund_history_allows_a_new_full_refund(PaymentRefundState state)
    {
        using PurchaseServiceShould.Fixture f = await Ready();
        PaymentSnapshot snapshot = f.Provider.Snapshot!;
        await using (AuthDbContext db = f.Db.CreateDbContext())
        {
            db.PaymentRefunds.Add(new PaymentRefund
            {
                Id = Guid.NewGuid(),
                PaymentAttemptId = snapshot.AttemptId,
                Provider = "alternate",
                ProviderAccountId = "merchant",
                Environment = "sandbox",
                OperationKey = "prior-key",
                RequestedBy = f.Account.Id,
                Reason = "Prior request",
                AmountMinor = 800,
                ExternalReference = "prior-refund",
                State = state,
                Unresolved = false,
                RequestedAt = f.Clock.Now
            });
            await db.SaveChangesAsync();
        }
        f.Provider.Snapshot = snapshot with { Refunds = [new("prior-refund", "payment", 800, "eur", state)] };
        RefundReply reply = await Service(f).RequestFullRefundAsync(f.Account.Id, snapshot.OrderId, snapshot.AttemptId, "New request");
        Assert.Equal("Pending", reply.State);
        Assert.NotEqual("prior-key", Assert.Single(f.Provider.RefundCommands).OperationKey);
        await using AuthDbContext read = f.Db.CreateDbContext();
        Assert.Equal(2, await read.PaymentRefunds.CountAsync());
        Assert.Equal("admin-refund", (await read.PaymentRefunds.SingleAsync(x => x.Id == reply.RefundId)).ExternalReference);
    }

    [Fact]
    public async Task Blocking_fresh_refund_state_does_not_reserve_an_undeliverable_operation()
    {
        using PurchaseServiceShould.Fixture f = await Ready();
        PaymentSnapshot snapshot = f.Provider.Snapshot!;
        await using (AuthDbContext db = f.Db.CreateDbContext())
        {
            db.PaymentRefunds.Add(new PaymentRefund
            {
                Id = Guid.NewGuid(),
                PaymentAttemptId = snapshot.AttemptId,
                Provider = "alternate",
                ProviderAccountId = "merchant",
                Environment = "sandbox",
                OperationKey = "prior-key",
                RequestedBy = f.Account.Id,
                Reason = "Prior request",
                AmountMinor = 800,
                ExternalReference = "prior-refund",
                State = PaymentRefundState.Failed,
                Unresolved = false,
                RequestedAt = f.Clock.Now
            });
            await db.SaveChangesAsync();
        }
        f.Provider.Snapshot = snapshot with { Refunds = [new("prior-refund", "payment", 800, "eur", PaymentRefundState.Pending)] };
        await Assert.ThrowsAsync<PurchaseException>(() => Service(f).RequestFullRefundAsync(f.Account.Id, snapshot.OrderId, snapshot.AttemptId, "New request"));
        Assert.Empty(f.Provider.RefundCommands);
        await using AuthDbContext read = f.Db.CreateDbContext();
        Assert.Equal(1, await read.PaymentRefunds.CountAsync());
    }

    private static async Task<PurchaseServiceShould.Fixture> Ready()
    {
        PurchaseServiceShould.Fixture f = await PaymentReconciliationShould.Setup();
        await PaymentReconciliationShould.Process(f);
        await using AuthDbContext db = f.Db.CreateDbContext();
        await db.Accounts.Where(x => x.Id == f.Account.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.AccessLevel, AccountAccessLevel.Player | AccountAccessLevel.Admin));
        return f;
    }

    private static PurchaseAdministrationService Service(PurchaseServiceShould.Fixture f) => new(new PurchaseRepository(f.Db, f.Clock),
        new PaymentProviderRegistry([f.Provider]), Options.Create(f.Config), f.Clock);
}
