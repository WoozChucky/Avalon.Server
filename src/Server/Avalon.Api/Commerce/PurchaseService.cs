using Avalon.Api.Contract.Commerce;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Commerce;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Commerce;

public interface IPurchaseService
{
    Task<AccountGameLicenseDto> GetLicenseStatusAsync(AccountId account, CancellationToken ct = default);
    Task<PurchaseOrderDto> GetOrderAsync(AccountId account, Guid orderId, CancellationToken ct = default);
    Task<CheckoutReply> CreateCheckoutAsync(AccountId account, int credentialsVersion, string source, CancellationToken ct = default);
}

public sealed class PurchaseService(IPurchaseRepository purchases, PaymentProviderRegistry providers, IOptions<CommerceConfiguration> options,
    IOptions<StoreAuthenticationConfiguration> authentication, ICheckoutBudget budget, TimeProvider clock) : IPurchaseService
{
    private CommerceConfiguration Config => options.Value;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<AccountGameLicenseDto> GetLicenseStatusAsync(AccountId account, CancellationToken ct = default)
    {
        // Existing production grants stay visible while commerce is disabled; the host owns authorization's environment.
        var status = await purchases.GetAccountStatusAsync(account, StoreAuthenticationConfiguration.Product, authentication.Value.Environment, ct);
        var license = status.License;
        var state = license is null ? "None" : license.RevokedAt is not null || license.ExpiresAt <= Now ? "Revoked" :
            license.SuspendedAt is not null ? "Suspended" : "Active";
        var order = await purchases.FindCurrentAsync(account, StoreAuthenticationConfiguration.Product, authentication.Value.Environment, ct);
        return new(state, status.EmailVerified, status.HasStoreLicense, Config.Enabled, Config.AmountMinor, Config.Currency, order?.Id);
    }

    public async Task<PurchaseOrderDto> GetOrderAsync(AccountId account, Guid orderId, CancellationToken ct = default)
    {
        var order = await purchases.FindForAccountAsync(account, orderId, ct) ?? throw new PurchaseException(PurchaseFailureCodes.NotFound);
        var attempt = await purchases.FindLatestAttemptAsync(orderId, ct);
        return new(order.Id, attempt?.State.ToString() ?? PaymentAttemptState.Reserved.ToString(), order.AmountMinor, order.Currency,
            order.TaxMinor, order.FulfilledAt is not null, order.ReversedAt is not null,
            order.ReconciliationIssue is not null || attempt?.State == PaymentAttemptState.NeedsReview, order.CreatedAt);
    }

    public async Task<CheckoutReply> CreateCheckoutAsync(AccountId account, int credentialsVersion, string source, CancellationToken ct = default)
    {
        if (!Config.Enabled) throw new PurchaseException(PurchaseFailureCodes.Disabled);
        if (string.IsNullOrWhiteSpace(source)) throw new PurchaseException(PurchaseFailureCodes.AccountUnavailable);
        var reservation = await purchases.ReserveAsync(new(account, credentialsVersion, Config.Product, Config.OfferId, Config.ProviderPriceId,
            Config.AmountMinor, Config.Currency, Config.Provider, Config.ProviderAccountId, Config.PaymentEnvironment, Config.LicenseEnvironment,
            Config.PublicSiteOrigin, string.Empty, Config.ProviderCatalogProductId, string.Join(',', Config.PaymentMethods), Now.Add(CommercePolicy.CheckoutLifetime)), ct);
        if (reservation.Error is { } error) throw new PurchaseException(error);
        var order = reservation.Order!;
        var attempt = reservation.Attempt!;
        if (attempt.State == PaymentAttemptState.CheckoutOpen)
            return await OwnedReply(account, order.Id, attempt.ExpiresAt > Now ? attempt.CheckoutUrl : null, ct);
        if (attempt.CheckoutReference is not null) return await OwnedReply(account, order.Id, null, ct);
        if (attempt.State is not (PaymentAttemptState.Reserved or PaymentAttemptState.ProviderUnknown)) return await OwnedReply(account, order.Id, null, ct);
        var claim = await purchases.ClaimAttemptAsync(attempt.Id, Now, TimeSpan.FromMinutes(2), ct);
        if (claim is null) return await OwnedReply(account, order.Id, null, ct);
        try
        {
            attempt = claim.Attempt;
            if (attempt.ReplayDeadline <= Now || order.Provider != Config.Provider || order.ProviderAccountId != Config.ProviderAccountId ||
                order.PaymentEnvironment != Config.PaymentEnvironment || order.LicenseEnvironment != Config.LicenseEnvironment)
            {
                await purchases.RecordUnknownAsync(claim, true, ct);
                throw new PurchaseException(PurchaseFailureCodes.NeedsReview);
            }
            if (attempt.FirstDispatchedAt is null && !await budget.TryTakeAsync(order.LicenseEnvironment, account, source, attempt.Id, ct))
                throw new PurchaseException(PurchaseFailureCodes.TooManyAttempts);
            claim = await purchases.BeginDispatchAsync(claim, ct);
            if (claim is null) return await OwnedReply(account, order.Id, null, ct);
            var provider = providers.Find(order.Provider) ?? throw new PurchaseException(PurchaseFailureCodes.ProviderUnavailable);
            attempt = claim.Attempt;
            try
            {
                var command = new CheckoutCreateCommand(order.Id, attempt.Id, attempt.OperationKey, attempt.ProviderPriceId, attempt.ProviderCatalogProductId,
                    order.AmountMinor, order.Currency, CommercePolicy.GameLicenseQuantity, attempt.CheckoutEmail, attempt.SuccessUrl, attempt.CancelUrl,
                    DateTime.SpecifyKind(attempt.RequestedExpiresAt, DateTimeKind.Utc), attempt.PaymentMethods.Split(','));
                var result = await provider.CreateCheckoutAsync(command, ct);
                // A recovered expired operation is not a usable checkout URL or permission to create another charge.
                if (await purchases.RecordCheckoutAsync(attempt.Id, claim.Version, new(result.CheckoutReference, result.CheckoutUrl, result.ExpiresAt), ct))
                    return await OwnedReply(account, order.Id, result.ExpiresAt > Now ? result.CheckoutUrl : null, ct);
                return await OwnedReply(account, order.Id, null, ct);
            }
            catch (PaymentProviderException)
            {
                await purchases.RecordUnknownAsync(claim, false, ct);
                return await OwnedReply(account, order.Id, null, ct);
            }
        }
        finally
        {
            await purchases.ReleaseAttemptAsync(attempt.Id, claim?.LeaseId ?? attempt.LeaseId!.Value, CancellationToken.None);
        }
    }

    private async Task<CheckoutReply> OwnedReply(AccountId account, Guid order, string? url, CancellationToken ct)
    {
        // Consolidation can move the beneficiary during an external call; the old browser must not receive its URL.
        if (await purchases.FindForAccountAsync(account, order, ct) is null) throw new PurchaseException(PurchaseFailureCodes.NotFound);
        return new(order, url);
    }
}
