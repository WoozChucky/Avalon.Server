using Avalon.Common.GameAuth;
using Avalon.Domain.Auth;
using Avalon.Domain.Commerce;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Auth.Repositories;

public sealed record PurchaseReconciliationCommand(PaymentAttemptClaim Claim, long ExpectedOrderVersion, PaymentSnapshot Snapshot, PaymentEventClaim? EventClaim = null);
public sealed record PurchaseReconciliationResult(bool Applied, string? FailureCode = null, bool NeedsReview = false);

public sealed partial class PurchaseRepository
{
    public async Task<PurchaseOrder?> FindOrderAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await Factory.CreateDbContextAsync(ct);
        return await db.PurchaseOrders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<PaymentAttempt?> ResolveAttemptAsync(string provider, string merchant, string environment, Guid? order, Guid? attempt,
        string? checkout, string? payment, CancellationToken ct = default)
    {
        await using var db = await Factory.CreateDbContextAsync(ct);
        var query = db.PaymentAttempts.AsNoTracking().Where(x => x.Provider == provider && x.ProviderAccountId == merchant && x.Environment == environment);
        if (order is not null && attempt is not null) query = query.Where(x => x.Id == attempt && x.OrderId == order);
        else if (checkout is not null) query = query.Where(x => x.CheckoutReference == checkout);
        else if (payment is not null) query = query.Where(x => x.PaymentReference == payment);
        else return null;
        return await query.SingleOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<PaymentAttempt>> FindSweepCandidatesAsync(int count, CancellationToken ct = default)
    {
        if (count is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(count));
        await using var db = await Factory.CreateDbContextAsync(ct);
        return await db.PaymentAttempts.AsNoTracking().Where(x => x.FirstDispatchedAt != null && (x.LeaseUntil == null || x.LeaseUntil <= Now))
            .OrderBy(x => x.LastReconciledAt != null).ThenBy(x => x.LastReconciledAt).ThenBy(x => x.CreatedAt).Take(count).ToListAsync(ct);
    }

    public async Task<PaymentQueueStats> ReadQueueStatsAsync(CancellationToken ct = default)
    {
        await using var db = await Factory.CreateDbContextAsync(ct);
        var pending = db.PaymentEvents.Where(x => x.State == PaymentEventState.Pending || x.State == PaymentEventState.Processing);
        return new(await pending.LongCountAsync(ct), await pending.LongCountAsync(x => x.RetryCount > 0, ct),
            await db.PaymentEvents.LongCountAsync(x => x.State == PaymentEventState.NeedsReview, ct), await pending.MinAsync(x => (DateTime?)x.CreatedAt, ct));
    }

    public async Task<bool> TouchReconciliationAsync(Guid attemptId, Guid leaseId, CancellationToken ct = default)
    {
        await using var db = await Factory.CreateDbContextAsync(ct);
        return await db.PaymentAttempts.Where(x => x.Id == attemptId && x.LeaseId == leaseId && x.LeaseUntil > Now && x.Version < long.MaxValue)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.LastReconciledAt, Now).SetProperty(x => x.Version, x => x.Version + 1), ct) == 1;
    }

    public async Task<PurchaseReconciliationResult> ApplySnapshotAsync(PurchaseReconciliationCommand command, CancellationToken ct = default)
    {
        await using var db = await Factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var initial = await db.PurchaseOrders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == command.Claim.Attempt.OrderId, ct);
        if (initial is null) return new(false, "ORDER_NOT_FOUND", true);
        await db.Accounts.Where(x => x.Id == initial.AccountId).ExecuteUpdateAsync(u => u.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch), ct);
        if (await db.Accounts.AnyAsync(x => x.Id == initial.AccountId && x.GameplayConsolidationId != null, ct)) return new(false, "CONSOLIDATION_PENDING");
        if (await db.PurchaseOrders.Where(x => x.Id == initial.Id && x.AccountId == initial.AccountId && x.Version == command.ExpectedOrderVersion && x.Version < long.MaxValue)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Version, x => x.Version), ct) != 1) return new(false, "STALE_ORDER");
        var claim = command.Claim;
        if (await db.PaymentAttempts.Where(x => x.Id == claim.Attempt.Id && x.Version == claim.Version && x.Version < long.MaxValue &&
            x.LeaseId == claim.LeaseId && x.LeaseUntil > Now).ExecuteUpdateAsync(u => u.SetProperty(x => x.Version, x => x.Version), ct) != 1)
            return new(false, "STALE_ATTEMPT");
        if (command.EventClaim is { } notification && await db.PaymentEvents.Where(x => x.Id == notification.Event.Id && x.Version == notification.Version &&
            x.LeaseId == notification.LeaseId && x.LeaseUntil > Now && x.State == PaymentEventState.Processing)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Version, x => x.Version), ct) != 1) return new(false, "STALE_EVENT");
        var order = await db.PurchaseOrders.SingleAsync(x => x.Id == initial.Id, ct);
        var attempt = await db.PaymentAttempts.SingleAsync(x => x.Id == claim.Attempt.Id, ct);
        var s = command.Snapshot;
        var bindingValid = s.OrderId == order.Id && s.AttemptId == attempt.Id && s.Provider == order.Provider && s.ProviderAccountId == order.ProviderAccountId &&
            s.PaymentEnvironment == order.PaymentEnvironment && s.CheckoutReference.Length is > 0 and <= 256 &&
            (attempt.CheckoutReference == null || attempt.CheckoutReference == s.CheckoutReference) &&
            (attempt.PaymentReference == null || attempt.PaymentReference == s.PaymentReference) && s.PriceReference == attempt.ProviderPriceId &&
            s.CatalogProductReference == attempt.ProviderCatalogProductId && s.Quantity == PurchaseLicense.Quantity && s.AmountMinor == order.AmountMinor &&
            s.Currency == order.Currency && Enum.IsDefined(s.State) && (s.Paid == (s.State == PaymentAttemptState.Paid)) &&
            s.Refunds.All(x => x.PaymentReference == s.PaymentReference && x.Currency == order.Currency && x.AmountMinor > 0 && x.AmountMinor <= order.AmountMinor && Enum.IsDefined(x.State)) &&
            s.Disputes.All(x => x.PaymentReference == s.PaymentReference && x.Currency == order.Currency && x.AmountMinor > 0 && x.AmountMinor <= order.AmountMinor && Enum.IsDefined(x.State));
        if (!bindingValid) return await Review(db, transaction, order, attempt, "INVALID_PAYMENT_BINDING", ct);
        if (s.Paid && (string.IsNullOrWhiteSpace(s.PaymentReference) || !s.TaxComplete || s.TaxMinor is null || s.TaxMinor < 0 || s.TaxMinor > s.AmountMinor ||
            s.SubtotalMinor is null || s.SubtotalMinor < 0 || s.SubtotalMinor > s.AmountMinor)) return await Review(db, transaction, order, attempt, "INCOMPLETE_PAYMENT_EVIDENCE", ct);
        if (await db.PaymentAttempts.AnyAsync(x => x.Id != attempt.Id && x.Provider == s.Provider && x.ProviderAccountId == s.ProviderAccountId && x.Environment == s.PaymentEnvironment &&
            (x.CheckoutReference == s.CheckoutReference || s.PaymentReference != null && x.PaymentReference == s.PaymentReference), ct))
            return await Review(db, transaction, order, attempt, "PAYMENT_ALREADY_BOUND", ct);

        attempt.CheckoutReference ??= s.CheckoutReference;
        attempt.PaymentReference ??= s.PaymentReference;
        attempt.ExpiresAt = s.ExpiresAt;
        attempt.LastReconciledAt = Now;
        // Paid is audit evidence even when financial access is reversed later.
        if (attempt.State != PaymentAttemptState.Paid) attempt.State = s.State;
        attempt.Version++;
        var refunds = await db.PaymentRefunds.Where(x => x.PaymentAttemptId == attempt.Id).ToListAsync(ct);
        foreach (var evidence in s.Refunds)
        {
            var refund = refunds.SingleOrDefault(x => x.ExternalReference == evidence.RefundReference);
            if (refund is null)
            {
                refund = new PaymentRefund
                {
                    Id = Guid.NewGuid(),
                    PaymentAttemptId = attempt.Id,
                    Provider = s.Provider,
                    ProviderAccountId = s.ProviderAccountId,
                    Environment = s.PaymentEnvironment,
                    OperationKey = Guid.NewGuid().ToString("N"),
                    RequestedBy = null,
                    Reason = "Observed provider reversal",
                    AmountMinor = evidence.AmountMinor,
                    ExternalReference = evidence.RefundReference,
                    State = evidence.State,
                    Unresolved = false,
                    RequestedAt = Now
                };
                db.PaymentRefunds.Add(refund); refunds.Add(refund);
            }
            else if (refund.State == PaymentRefundState.Succeeded && evidence.State != PaymentRefundState.Succeeded)
            {
                // Terminal revocation stands; retain the contradictory observation for manual reconciliation.
                refund.FailureCode = "REFUND_SUCCESS_REPORTED_" + evidence.State.ToString().ToUpperInvariant();
                refund.Version++;
            }
            else if (refund.State != PaymentRefundState.Succeeded && refund.State != evidence.State) { refund.State = evidence.State; refund.Version++; }
            refund.ObservedAt = Now;
            if (refund.State is PaymentRefundState.Succeeded or PaymentRefundState.Failed or PaymentRefundState.Canceled) refund.Unresolved = false;
        }
        var disputes = await db.PaymentDisputes.Where(x => x.PaymentAttemptId == attempt.Id).ToListAsync(ct);
        foreach (var evidence in s.Disputes)
        {
            var dispute = disputes.SingleOrDefault(x => x.ExternalReference == evidence.DisputeReference);
            if (dispute is null)
            {
                dispute = new PaymentDispute
                {
                    Id = Guid.NewGuid(),
                    PaymentAttemptId = attempt.Id,
                    Provider = s.Provider,
                    ProviderAccountId = s.ProviderAccountId,
                    Environment = s.PaymentEnvironment,
                    ExternalReference = evidence.DisputeReference,
                    State = evidence.State,
                    CreatedAt = Now,
                    ObservedAt = Now
                };
                db.PaymentDisputes.Add(dispute); disputes.Add(dispute);
            }
            else if (dispute.State is not (PaymentDisputeState.Lost or PaymentDisputeState.Accepted) && dispute.State != evidence.State)
            { dispute.State = evidence.State; dispute.Version++; }
            dispute.ObservedAt = Now;
        }
        var refunded = refunds.Where(x => x.State == PaymentRefundState.Succeeded).Sum(x => (decimal)x.AmountMinor) >= order.AmountMinor;
        var lost = disputes.Any(x => x.State is PaymentDisputeState.Lost or PaymentDisputeState.Accepted);
        var contradictoryRefund = refunds.Any(x => x.FailureCode?.StartsWith("REFUND_SUCCESS_REPORTED_", StringComparison.Ordinal) == true);
        var needsReview = contradictoryRefund || refunds.Any(x => x.AmountMinor != order.AmountMinor) || disputes.Any(x => x.State == PaymentDisputeState.Inquiry);
        if (needsReview) order.ReconciliationIssue = contradictoryRefund ? "REFUND_SUCCESS_CONTRADICTION" :
            refunds.Any(x => x.AmountMinor != order.AmountMinor) ? "UNSUPPORTED_PARTIAL_REFUND" : "DISPUTE_INQUIRY";
        var funding = order.FundingAttemptId == attempt.Id;
        GameLicense? license = order.LicenseId is { } licenseId ? await db.GameLicenses.SingleAsync(x => x.Id == licenseId, ct) : null;
        if (license is not null && (license.AccountId != order.AccountId || license.LicenseReference != PurchaseLicense.Reference(order.Id) ||
            license.Provider != PurchaseLicense.Provider || license.Environment != order.LicenseEnvironment || license.Product != order.Product || license.AuthorityKind != LicenseAuthorityKind.StoredGrant))
            return await Review(db, transaction, order, attempt, "INVALID_LICENSE_BINDING", ct);
        if (s.Paid && !refunded && !lost && order.ReversedAt is null && order.FundingAttemptId is null)
        {
            license = new GameLicense
            {
                Id = Guid.NewGuid(),
                AccountId = order.AccountId,
                Provider = PurchaseLicense.Provider,
                Environment = order.LicenseEnvironment,
                Product = order.Product,
                ProviderProductId = PurchaseLicense.ProviderProduct,
                LicenseReference = PurchaseLicense.Reference(order.Id),
                AuthorityKind = LicenseAuthorityKind.StoredGrant,
                GrantedAt = Now
            };
            db.GameLicenses.Add(license);
            order.LicenseId = license.Id; order.FundingAttemptId = attempt.Id; order.FulfilledAt = Now; funding = true;
            order.TaxMinor = s.TaxMinor; order.SubtotalMinor = s.SubtotalMinor;
        }
        else if (s.Paid && order.FundingAttemptId is { } funded && funded != attempt.Id && !refunded)
        { order.ReconciliationIssue = "DUPLICATE_PAYMENT"; needsReview = true; }
        if ((funding || order.FundingAttemptId is null) && (refunded || lost))
        {
            order.ReversedAt ??= Now; order.Unresolved = false;
            if (license is not null && license.RevokedAt is null) { license.RevokedAt = Now; license.AuthorityRevision++; }
        }
        else if (!s.Paid && order.FundingAttemptId is null && attempt.State is PaymentAttemptState.Failed or PaymentAttemptState.Expired or PaymentAttemptState.Canceled)
        { /* Keep this order reserved; the next eligible request can create a new attempt using its frozen offer. */ }
        order.Version++;
        // Persist newly allocated license before the same-transaction helper locks it; nothing commits yet.
        await db.SaveChangesAsync(ct);
        if (funding && license is not null)
        {
            foreach (var dispute in disputes)
                await LicenseHoldMutations.SetAsync(db, license.Id, PurchaseLicense.DisputeHoldCause, dispute.Id.ToString("N"),
                    dispute.State is PaymentDisputeState.Open or PaymentDisputeState.UnderReview, Now, ct);
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return new(true, needsReview ? order.ReconciliationIssue : null, needsReview);
    }

    private async Task<PurchaseReconciliationResult> Review(AuthDbContext db, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        PurchaseOrder order, PaymentAttempt attempt, string reason, CancellationToken ct)
    {
        order.ReconciliationIssue = reason; order.Version++;
        attempt.State = PaymentAttemptState.NeedsReview; attempt.LastReconciledAt = Now; attempt.Version++;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(true, reason, true);
    }
}
