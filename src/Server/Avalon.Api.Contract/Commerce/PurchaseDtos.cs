namespace Avalon.Api.Contract.Commerce;

public sealed record AccountGameLicenseDto(string State, bool EmailVerified, bool HasStoreLicense, bool CheckoutEnabled,
    long AmountMinor, string Currency, Guid? CurrentOrderId, bool SandboxCheckout = false);
public sealed record PurchaseOrderDto(Guid OrderId, string State, long AmountMinor, string Currency,
    long? TaxMinor, bool Fulfilled, bool Reversed, bool NeedsReview, DateTime CreatedAt);
public sealed record CheckoutReply(Guid OrderId, string? CheckoutUrl);
