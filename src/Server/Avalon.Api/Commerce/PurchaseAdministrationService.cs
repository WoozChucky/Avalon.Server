using Avalon.Api.Contract.Commerce;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Commerce;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Commerce;

public interface IPurchaseAdministrationService
{
    Task<PagedResult<AdminPurchaseDto>> SearchAsync(PurchaseSearch search, CancellationToken ct = default);
    Task<AdminPurchaseDetailDto> GetAsync(Guid order, CancellationToken ct = default);
    Task<RefundReply> RequestFullRefundAsync(AccountId admin, Guid order, Guid attempt, string reason, CancellationToken ct = default);
    Task RetryReconciliationAsync(AccountId admin, Guid order, CancellationToken ct = default);
}

public sealed class PurchaseAdministrationService(IPurchaseRepository purchases, PaymentProviderRegistry providers,
    IOptions<CommerceConfiguration> options, TimeProvider clock) : IPurchaseAdministrationService
{
    public async Task<PagedResult<AdminPurchaseDto>> SearchAsync(PurchaseSearch search, CancellationToken ct = default)
    {
        var page = await purchases.SearchOrdersAsync(new(search.Page, search.PageSize, search.AccountId, search.State, search.Environment), ct);
        return new(page.Page, page.PageSize, page.TotalCount, page.Items.Select(Map).ToList());
    }
    public async Task<AdminPurchaseDetailDto> GetAsync(Guid order, CancellationToken ct = default)
    {
        var detail = await purchases.FindDetailsAsync(order, ct) ?? throw new PurchaseException(PurchaseFailureCodes.NotFound);
        var license = detail.License;
        var state = license is null ? "None" : license.RevokedAt is not null ? "Revoked" : license.SuspendedAt is not null ? "Suspended" : "Active";
        return new(Map(detail.Order), detail.BeneficiaryUsername, detail.PurchaserUsername, state,
            detail.Attempts.Select(x => new AdminPaymentAttemptDto(x.Id, x.Sequence, x.State.ToString(), x.Id == detail.Order.FundingAttemptId, x.CheckoutReference, x.PaymentReference, x.CreatedAt, x.LastReconciledAt)).ToArray(),
            detail.Refunds.Select(x => new AdminRefundDto(x.Id, x.PaymentAttemptId, x.State.ToString(), x.Unresolved, x.AmountMinor, x.RequestedBy?.Value, x.Reason, x.ExternalReference, x.FailureCode, x.RequestedAt)).ToArray(),
            detail.Disputes.Select(x => new AdminDisputeDto(x.Id, x.PaymentAttemptId, x.State.ToString(), x.ExternalReference, x.ObservedAt)).ToArray(),
            detail.Events.Select(x => new AdminPaymentEventDto(x.Id, x.State.ToString(), x.RetryCount, x.FailureCode, x.NextAttemptAt)).ToArray());
    }
    public async Task<RefundReply> RequestFullRefundAsync(AccountId admin, Guid orderId, Guid attemptId, string reason, CancellationToken ct = default)
    {
        if (!await purchases.IsAdministratorAsync(admin, ct)) throw new PurchaseException(PurchaseFailureCodes.AccountUnavailable);
        if (!options.Value.Enabled) throw new PurchaseException(PurchaseFailureCodes.Disabled);
        reason = reason.Trim();
        if (reason.Length is < 1 or > 500) throw new PurchaseException(PurchaseFailureCodes.InvalidRequest);
        var detail = await purchases.FindDetailsAsync(orderId, ct) ?? throw new PurchaseException(PurchaseFailureCodes.NotFound);
        var attempt = detail.Attempts.SingleOrDefault(x => x.Id == attemptId);
        if (attempt is not { State: PaymentAttemptState.Paid, PaymentReference: not null }) throw new PurchaseException(PurchaseFailureCodes.NotFound);
        var order = detail.Order;
        var config = options.Value;
        if (order.Provider != config.Provider || order.ProviderAccountId != config.ProviderAccountId || order.PaymentEnvironment != config.PaymentEnvironment || order.LicenseEnvironment != config.LicenseEnvironment)
            throw new PurchaseException(PurchaseFailureCodes.NeedsReview);
        var claim = await purchases.ClaimAttemptAsync(attempt.Id, clock.GetUtcNow().UtcDateTime, TimeSpan.FromMinutes(2), ct);
        if (claim is null) throw new PurchaseException(PurchaseFailureCodes.NeedsReview);
        try
        {
            order = await purchases.FindOrderAsync(orderId, ct) ?? throw new PurchaseException(PurchaseFailureCodes.NotFound);
            var provider = providers.Find(order.Provider) ?? throw new PurchaseException(PurchaseFailureCodes.ProviderUnavailable);
            var snapshot = await provider.GetCheckoutAsync(new(attempt.CheckoutReference, attempt.PaymentReference), ct);
            if (!snapshot.Paid || snapshot.OrderId != orderId || snapshot.AttemptId != attemptId || snapshot.Provider != order.Provider || snapshot.ProviderAccountId != order.ProviderAccountId ||
                snapshot.PaymentEnvironment != order.PaymentEnvironment || snapshot.PaymentReference != attempt.PaymentReference || snapshot.CheckoutReference != attempt.CheckoutReference ||
                snapshot.AmountMinor != order.AmountMinor || snapshot.Currency != order.Currency || snapshot.PriceReference != attempt.ProviderPriceId ||
                snapshot.CatalogProductReference != attempt.ProviderCatalogProductId || snapshot.Quantity != CommercePolicy.GameLicenseQuantity ||
                snapshot.Disputes.Any(x => x.State is PaymentDisputeState.Open or PaymentDisputeState.UnderReview)) throw new PurchaseException(PurchaseFailureCodes.NeedsReview);
            // Only a known local operation can explain an observed provider refund; never reserve a new request over one.
            if (snapshot.Refunds.Count > 0 && !detail.Refunds.Any(x => x.PaymentAttemptId == attemptId && x.ExternalReference is not null &&
                snapshot.Refunds.Any(observed => observed.RefundReference == x.ExternalReference))) throw new PurchaseException(PurchaseFailureCodes.NeedsReview);
            var reserved = await purchases.ReserveRefundAsync(admin, orderId, order.Version, claim, reason, ct);
            if (reserved.Error is { } error) throw new PurchaseException(error);
            var refund = reserved.Refund!;
            if (refund.ExternalReference is not null) return Reply(refund);
            // An externally observed reversal cannot be guessed to be this unknown administrator operation.
            if (snapshot.Refunds.Count > 0) throw new PurchaseException(PurchaseFailureCodes.NeedsReview);
            refund = await purchases.BeginRefundDispatchAsync(refund.Id, refund.Version, claim, ct) ?? throw new PurchaseException(PurchaseFailureCodes.NeedsReview);
            try
            {
                var result = await provider.RequestFullRefundAsync(new(refund.OperationKey, attempt.PaymentReference, refund.AmountMinor, order.Currency), ct);
                if (result.Currency != order.Currency) throw new PaymentProviderException("INVALID_REFUND_BINDING");
                if (!await purchases.RecordRefundOutcomeAsync(refund, claim, result, ct)) throw new PurchaseException(PurchaseFailureCodes.NeedsReview);
                refund.State = result.State;
                await purchases.ScheduleReconciliationAsync(admin, orderId, ct);
                return Reply(refund);
            }
            catch (PaymentProviderException)
            {
                await purchases.RecordRefundOutcomeAsync(refund, claim, null, ct);
                return Reply(refund);
            }
        }
        catch (PaymentProviderException) { throw new PurchaseException(PurchaseFailureCodes.ProviderUnavailable); }
        finally { await purchases.ReleaseAttemptAsync(attemptId, claim.LeaseId, CancellationToken.None); }
    }
    public async Task RetryReconciliationAsync(AccountId admin, Guid order, CancellationToken ct = default)
    {
        if (!await purchases.IsAdministratorAsync(admin, ct)) throw new PurchaseException(PurchaseFailureCodes.AccountUnavailable);
        if (await purchases.FindOrderAsync(order, ct) is null) throw new PurchaseException(PurchaseFailureCodes.NotFound);
        await purchases.ScheduleReconciliationAsync(admin, order, ct);
    }
    private static RefundReply Reply(PaymentRefund refund) => new(refund.Id, refund.State.ToString(), refund.State == PaymentRefundState.NeedsReview);
    private static AdminPurchaseDto Map(PurchaseOrder x) => new(x.Id, x.AccountId.Value, x.OriginalPurchaserAccountId.Value, x.Product, x.Provider,
        x.PaymentEnvironment, x.LicenseEnvironment, x.AmountMinor, x.Currency, x.TaxMinor, x.ReversedAt is not null ? "reversed" : x.FulfilledAt is not null ? "fulfilled" : "pending",
        x.FundingAttemptId, x.LicenseId, x.ReconciliationIssue, x.CreatedAt);
}
