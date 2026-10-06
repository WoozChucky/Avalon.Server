using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Commerce;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Commerce;

public interface IPaymentReconciliationService
{
    Task<PaymentProcessingResult> ProcessAsync(PaymentEventClaim claim, CancellationToken ct);
    Task<PaymentProcessingResult> SweepAsync(PaymentAttempt attempt, CancellationToken ct);
}
public sealed class PaymentReconciliationService(IPurchaseRepository purchases, PaymentProviderRegistry providers,
    IOptions<CommerceConfiguration> options, TimeProvider clock) : IPaymentReconciliationService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public async Task<PaymentProcessingResult> ProcessAsync(PaymentEventClaim claim, CancellationToken ct)
    {
        var row = claim.Event;
        var config = options.Value;
        if (!config.Enabled || row.Provider != config.Provider || row.ProviderAccountId != config.ProviderAccountId || row.Environment != config.PaymentEnvironment)
            return new(false, "INVALID_NOTIFICATION_SCOPE", true);
        var provider = providers.Find(row.Provider);
        if (provider is null) return new(false, "PROVIDER_UNAVAILABLE");
        var lookup = new PaymentLookup(row.ResourceKind == PaymentResourceKinds.Checkout ? row.ResourceReference : null, row.PaymentReference);
        try
        {
            var attempt = await purchases.ResolveAttemptAsync(row.Provider, row.ProviderAccountId, row.Environment, row.OrderId, row.PaymentAttemptId,
                lookup.CheckoutReference, lookup.PaymentReference, ct);
            if (attempt is null)
            {
                // Discovery provides locators only. Acquire the local fence, then fetch again before using any evidence.
                var locator = await provider.GetCheckoutAsync(lookup, ct);
                attempt = await purchases.ResolveAttemptAsync(row.Provider, row.ProviderAccountId, row.Environment, locator.OrderId, locator.AttemptId, null, null, ct);
            }
            if (attempt is null) return new(false, "UNKNOWN_PAYMENT", true);
            if (row.ResourceKind == PaymentResourceKinds.Reconciliation)
                lookup = new(attempt.CheckoutReference, attempt.PaymentReference);
            return await Reconcile(attempt, lookup, provider, claim, ct);
        }
        catch (PaymentProviderException) { return new(false, "PROVIDER_UNAVAILABLE"); }
    }

    public async Task<PaymentProcessingResult> SweepAsync(PaymentAttempt attempt, CancellationToken ct)
    {
        var config = options.Value;
        if (!config.Enabled || attempt.Provider != config.Provider || attempt.ProviderAccountId != config.ProviderAccountId || attempt.Environment != config.PaymentEnvironment)
            return new(false, "INVALID_PAYMENT_SCOPE", true);
        var provider = providers.Find(attempt.Provider);
        if (provider is null) return new(false, "PROVIDER_UNAVAILABLE");
        return await Reconcile(attempt, new(attempt.CheckoutReference, attempt.PaymentReference), provider, null, ct);
    }

    private async Task<PaymentProcessingResult> Reconcile(PaymentAttempt original, PaymentLookup lookup, IPaymentProvider provider,
        PaymentEventClaim? notification, CancellationToken ct)
    {
        var claim = await purchases.ClaimAttemptAsync(original.Id, Now, TimeSpan.FromMinutes(2), ct);
        if (claim is null) return new(false, "ATTEMPT_BUSY");
        var leaseId = claim.LeaseId;
        try
        {
            var order = await purchases.FindOrderAsync(original.OrderId, ct);
            if (order is null) return new(false, "ORDER_NOT_FOUND", true);
            if (order.LicenseEnvironment != options.Value.LicenseEnvironment) return new(false, "INVALID_LICENSE_SCOPE", true);
            if (lookup.CheckoutReference is null && lookup.PaymentReference is null)
            {
                // Only a previously dispatched unknown may replay. A sweep never creates a new operation or takes a new budget slot.
                if (claim.Attempt.FirstDispatchedAt is null) return new(false, "NOT_DISPATCHED");
                if (claim.Attempt.ReplayDeadline <= Now || claim.Attempt.State == PaymentAttemptState.NeedsReview)
                {
                    await purchases.RecordUnknownAsync(claim, true, ct);
                    return new(false, "UNKNOWN_RESULT_EXPIRED", true);
                }
                claim = await purchases.BeginDispatchAsync(claim, ct);
                if (claim is null) return new(false, "STALE_ATTEMPT");
                var a = claim.Attempt;
                var recovered = await provider.CreateCheckoutAsync(new(order.Id, a.Id, a.OperationKey, a.ProviderPriceId, a.ProviderCatalogProductId,
                    order.AmountMinor, order.Currency, CommercePolicy.GameLicenseQuantity, a.CheckoutEmail, a.SuccessUrl, a.CancelUrl,
                    DateTime.SpecifyKind(a.RequestedExpiresAt, DateTimeKind.Utc), a.PaymentMethods.Split(',')), ct);
                lookup = new(recovered.CheckoutReference, null);
            }
            var snapshot = await provider.GetCheckoutAsync(lookup, ct);
            var applied = await purchases.ApplySnapshotAsync(new(claim, order.Version, snapshot, notification), ct);
            return new(applied.Applied, applied.FailureCode, applied.NeedsReview);
        }
        catch (PaymentProviderException) { return new(false, "PROVIDER_UNAVAILABLE"); }
        finally
        {
            try { await purchases.TouchReconciliationAsync(original.Id, leaseId, CancellationToken.None); }
            finally { await purchases.ReleaseAttemptAsync(original.Id, leaseId, CancellationToken.None); }
        }
    }
}
