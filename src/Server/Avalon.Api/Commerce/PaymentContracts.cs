using Avalon.Domain.Commerce;

namespace Avalon.Api.Commerce;

public sealed record CheckoutCreateCommand(Guid OrderId, Guid AttemptId, string OperationKey, string PriceReference,
    string CatalogProductReference, long AmountMinor, string Currency, int Quantity, string Email, string SuccessUrl,
    string CancelUrl, DateTime ExpiresAt, IReadOnlyList<string> PaymentMethods);
public sealed record CheckoutProviderResult(string CheckoutReference, string CheckoutUrl, DateTime ExpiresAt);
public sealed record PaymentLookup(string? CheckoutReference, string? PaymentReference);
public sealed record FullRefundCommand(string OperationKey, string PaymentReference, long AmountMinor, string Currency);
public sealed record VerifiedPaymentNotification(string Provider, string ProviderAccountId, string PaymentEnvironment,
    string EventReference, string Type, string ResourceKind, string ResourceReference, string? PaymentReference, Guid? OrderId, Guid? AttemptId, DateTime CreatedAt);

/// <summary>Safe boundary failure; provider exception bodies and request values are intentionally discarded.</summary>
public sealed class PaymentProviderException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
