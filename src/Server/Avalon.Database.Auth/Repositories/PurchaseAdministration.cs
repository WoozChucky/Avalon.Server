using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Avalon.Domain.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Avalon.Database.Auth.Repositories;

public sealed record PurchaseSearchFilter(int Page, int PageSize, long? AccountId, string? State, string? Environment);
public sealed record PurchaseDetails(PurchaseOrder Order, string BeneficiaryUsername, string PurchaserUsername, IReadOnlyList<PaymentAttempt> Attempts,
    IReadOnlyList<PaymentRefund> Refunds, IReadOnlyList<PaymentDispute> Disputes, IReadOnlyList<PaymentEvent> Events, GameLicense? License);
public sealed record RefundReservationResult(string? Error, PaymentRefund? Refund = null);

public sealed partial class PurchaseRepository
{
    public async Task<bool> IsAdministratorAsync(AccountId admin, CancellationToken ct = default)
    {
        await using AuthDbContext db = await Factory.CreateDbContextAsync(ct);
        return await db.Accounts.AnyAsync(x => x.Id == admin && x.Status == AccountStatus.Active &&
            (x.AccessLevel & AccountAccessLevel.Admin) != 0 && (!x.Locked || x.LockedUntil <= Now), ct);
    }

    public async Task<PagedResult<PurchaseOrder>> SearchOrdersAsync(PurchaseSearchFilter filter, CancellationToken ct = default)
    {
        if (filter.Page < 1 || filter.PageSize is < 1 or > 100 || filter.Page > int.MaxValue / filter.PageSize) throw new ArgumentException("Invalid purchase page.");
        await using AuthDbContext db = await Factory.CreateDbContextAsync(ct);
        IQueryable<PurchaseOrder> query = db.PurchaseOrders.AsNoTracking().AsQueryable();
        if (filter.AccountId is { } account) query = query.Where(x => x.AccountId == new AccountId(account) || x.OriginalPurchaserAccountId == new AccountId(account));
        if (filter.Environment is { } environment) query = query.Where(x => x.PaymentEnvironment == environment);
        query = filter.State switch
        {
            "pending" => query.Where(x => x.FulfilledAt == null && x.ReversedAt == null),
            "fulfilled" => query.Where(x => x.FulfilledAt != null && x.ReversedAt == null),
            "reversed" => query.Where(x => x.ReversedAt != null),
            "needs-review" => query.Where(x => x.ReconciliationIssue != null),
            null or "" => query,
            _ => throw new ArgumentException("Invalid purchase state."),
        };
        int total = await query.CountAsync(ct);
        List<PurchaseOrder> items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToListAsync(ct);
        return new(filter.Page, filter.PageSize, total, items);
    }

    public async Task<PurchaseDetails?> FindDetailsAsync(Guid id, CancellationToken ct = default)
    {
        await using AuthDbContext db = await Factory.CreateDbContextAsync(ct);
        PurchaseOrder? order = await db.PurchaseOrders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (order is null) return null;
        List<PaymentAttempt> attempts = await db.PaymentAttempts.AsNoTracking().Where(x => x.OrderId == id).OrderBy(x => x.Sequence).ToListAsync(ct);
        Guid[] ids = attempts.Select(x => x.Id).ToArray();
        List<PaymentRefund> refunds = await db.PaymentRefunds.AsNoTracking().Where(x => ids.Contains(x.PaymentAttemptId)).OrderBy(x => x.RequestedAt).ToListAsync(ct);
        List<PaymentDispute> disputes = await db.PaymentDisputes.AsNoTracking().Where(x => ids.Contains(x.PaymentAttemptId)).ToListAsync(ct);
        List<PaymentEvent> events = await db.PaymentEvents.AsNoTracking().Where(x => x.OrderId == id).OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(ct);
        GameLicense? license = order.LicenseId is { } licenseId ? await db.GameLicenses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == licenseId, ct) : null;
        string beneficiary = await db.Accounts.Where(x => x.Id == order.AccountId).Select(x => x.Username).SingleAsync(ct);
        string purchaser = await db.Accounts.Where(x => x.Id == order.OriginalPurchaserAccountId).Select(x => x.Username).SingleAsync(ct);
        return new(order, beneficiary, purchaser, attempts, refunds, disputes, events, license);
    }

    public async Task<RefundReservationResult> ReserveRefundAsync(AccountId admin, Guid orderId, long orderVersion, PaymentAttemptClaim claim, string reason, CancellationToken ct = default)
    {
        await using AuthDbContext db = await Factory.CreateDbContextAsync(ct);
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        PurchaseOrder? initial = await db.PurchaseOrders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == orderId, ct);
        if (initial is null) return new(PurchaseFailureCodes.NotFound);
        foreach (AccountId? id in new[] { admin, initial.AccountId }.Distinct().OrderBy(x => x.Value))
            await db.Accounts.Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch), ct);
        if (!await db.Accounts.AnyAsync(x => x.Id == admin && x.Status == AccountStatus.Active && (x.AccessLevel & AccountAccessLevel.Admin) != 0 &&
            (!x.Locked || x.LockedUntil <= Now), ct)) return new(PurchaseFailureCodes.AccountUnavailable);
        if (await db.PurchaseOrders.Where(x => x.Id == orderId && x.AccountId == initial.AccountId && x.Version == orderVersion)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Version, x => x.Version), ct) != 1) return new(PurchaseFailureCodes.NeedsReview);
        PaymentAttempt? attempt = await db.PaymentAttempts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == claim.Attempt.Id && x.OrderId == orderId &&
            x.Version == claim.Version && x.LeaseId == claim.LeaseId && x.LeaseUntil > Now, ct);
        if (attempt is not { State: PaymentAttemptState.Paid, PaymentReference: not null }) return new(PurchaseFailureCodes.NotFound);
        PaymentRefund? pending = await db.PaymentRefunds.SingleOrDefaultAsync(x => x.PaymentAttemptId == attempt.Id && x.Unresolved, ct);
        if (pending is not null)
        {
            if (pending.ExternalReference is null && pending.ReplayDeadline <= Now)
            {
                pending.State = PaymentRefundState.NeedsReview; pending.FailureCode = "UNKNOWN_RESULT_EXPIRED"; pending.Version++;
                await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
                return new(PurchaseFailureCodes.NeedsReview, pending);
            }
            await transaction.CommitAsync(ct);
            return new(null, pending);
        }
        PaymentRefund? known = await db.PaymentRefunds.AsNoTracking().Where(x => x.PaymentAttemptId == attempt.Id && x.State == PaymentRefundState.Succeeded).FirstOrDefaultAsync(ct);
        if (known is not null) { await transaction.CommitAsync(ct); return new(null, known); }
        var refund = new PaymentRefund
        {
            Id = Guid.NewGuid(),
            PaymentAttemptId = attempt.Id,
            Provider = initial.Provider,
            ProviderAccountId = initial.ProviderAccountId,
            Environment = initial.PaymentEnvironment,
            OperationKey = Guid.NewGuid().ToString("N"),
            RequestedBy = admin,
            Reason = reason,
            AmountMinor = initial.AmountMinor,
            RequestedAt = Now,
            State = PaymentRefundState.Pending
        };
        db.PaymentRefunds.Add(refund);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return new(null, refund);
    }

    public async Task<PaymentRefund?> BeginRefundDispatchAsync(Guid refundId, long version, PaymentAttemptClaim claim, CancellationToken ct = default)
    {
        await using AuthDbContext db = await Factory.CreateDbContextAsync(ct);
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        if (await db.PaymentAttempts.Where(x => x.Id == claim.Attempt.Id && x.Version == claim.Version && x.LeaseId == claim.LeaseId && x.LeaseUntil > Now)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Version, x => x.Version), ct) != 1) return null;
        if (await db.PaymentRefunds.Where(x => x.Id == refundId && x.Version == version && x.Version < long.MaxValue && x.Unresolved && x.ExternalReference == null &&
            x.State == PaymentRefundState.Pending && (x.ReplayDeadline == null || x.ReplayDeadline > Now))
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.FirstDispatchedAt, x => x.FirstDispatchedAt ?? Now)
                .SetProperty(x => x.ReplayDeadline, x => x.ReplayDeadline ?? Now.AddHours(23)).SetProperty(x => x.Version, x => x.Version + 1), ct) != 1) return null;
        PaymentRefund row = await db.PaymentRefunds.AsNoTracking().SingleAsync(x => x.Id == refundId, ct);
        await transaction.CommitAsync(ct); return row;
    }

    public async Task<bool> RecordRefundOutcomeAsync(PaymentRefund operation, PaymentAttemptClaim claim, RefundProviderResult? result, CancellationToken ct = default)
    {
        await using AuthDbContext db = await Factory.CreateDbContextAsync(ct);
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        if (await db.PaymentAttempts.Where(x => x.Id == claim.Attempt.Id && x.Version == claim.Version && x.LeaseId == claim.LeaseId && x.LeaseUntil > Now)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Version, x => x.Version), ct) != 1) return false;
        PaymentRefund? row = await db.PaymentRefunds.SingleOrDefaultAsync(x => x.Id == operation.Id && x.Version == operation.Version, ct);
        if (row is null) return false;
        row.Version++;
        if (result is null) row.FailureCode = PurchaseFailureCodes.ProviderUnavailable;
        else
        {
            if (result.PaymentReference != claim.Attempt.PaymentReference || result.AmountMinor != row.AmountMinor || !Text(result.RefundReference, 256) || !Enum.IsDefined(result.State)) return false;
            if (await db.PaymentRefunds.AnyAsync(x => x.Id != row.Id && x.Provider == row.Provider && x.ProviderAccountId == row.ProviderAccountId &&
                x.Environment == row.Environment && x.ExternalReference == result.RefundReference, ct))
            { row.State = PaymentRefundState.NeedsReview; row.FailureCode = "AMBIGUOUS_REFUND_BINDING"; }
            else
            {
                row.ExternalReference = result.RefundReference; row.State = result.State; row.ObservedAt = Now; row.FailureCode = null;
                row.Unresolved = result.State is PaymentRefundState.Pending or PaymentRefundState.NeedsReview;
            }
        }
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return true;
    }

    public async Task<bool> ScheduleReconciliationAsync(AccountId admin, Guid orderId, CancellationToken ct = default)
    {
        if (!await IsAdministratorAsync(admin, ct)) return false;
        await using AuthDbContext db = await Factory.CreateDbContextAsync(ct);
        List<PaymentAttempt> attempts = await db.PaymentAttempts.AsNoTracking().Where(x => x.OrderId == orderId).ToListAsync(ct);
        foreach (PaymentAttempt? attempt in attempts)
        {
            db.PaymentEvents.Add(new PaymentEvent
            {
                Id = Guid.NewGuid(),
                Provider = attempt.Provider,
                ProviderAccountId = attempt.ProviderAccountId,
                Environment = attempt.Environment,
                ExternalReference = Guid.NewGuid().ToString("N"),
                Type = "reconciliation-requested",
                ResourceKind = PaymentResourceKinds.Reconciliation,
                ResourceReference = attempt.Id.ToString("N"),
                PaymentReference = attempt.PaymentReference,
                OrderId = orderId,
                PaymentAttemptId = attempt.Id,
                CreatedAt = Now,
                NextAttemptAt = Now
            });
        }
        await db.SaveChangesAsync(ct); return attempts.Count > 0;
    }
}
