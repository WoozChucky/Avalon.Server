namespace Avalon.Domain.Commerce;

/// <summary>Normalized provider evidence; no SDK type or secret crosses this boundary.</summary>
public sealed record RefundProviderResult(string RefundReference, string PaymentReference, long AmountMinor, string Currency, PaymentRefundState State);
public sealed record PaymentDisputeSnapshot(string DisputeReference, string PaymentReference, long AmountMinor, string Currency, PaymentDisputeState State);
public sealed record PaymentSnapshot(string Provider, string ProviderAccountId, string PaymentEnvironment, Guid OrderId, Guid AttemptId,
    string CheckoutReference, string? PaymentReference, string PriceReference, string CatalogProductReference, int Quantity,
    long AmountMinor, string Currency, long? TaxMinor, long? SubtotalMinor, bool TaxComplete, bool Paid, PaymentAttemptState State,
    DateTime ExpiresAt, IReadOnlyList<RefundProviderResult> Refunds, IReadOnlyList<PaymentDisputeSnapshot> Disputes);

public static class PaymentResourceKinds
{
    public const string Checkout = "checkout";
    public const string Refund = "refund";
    public const string Dispute = "dispute";
    public const string Reconciliation = "reconciliation";
}

public static class PurchaseLicense
{
    public const string Provider = "avalon";
    public const string ProviderProduct = "base";
    public const int Quantity = 1;
    public const string DisputeHoldCause = "payment-dispute";
    public static string Reference(Guid order) => $"purchase:{order:N}";
}
