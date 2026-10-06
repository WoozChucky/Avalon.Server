namespace Avalon.Api.Commerce;

public interface IPaymentProvider
{
    string Provider { get; }
    Task<CheckoutProviderResult> CreateCheckoutAsync(CheckoutCreateCommand command, CancellationToken ct);
    Task<PaymentSnapshot> GetCheckoutAsync(PaymentLookup lookup, CancellationToken ct);
    VerifiedPaymentNotification VerifyNotification(ReadOnlyMemory<byte> body, IReadOnlyDictionary<string, string> headers, DateTime now);
    Task<RefundProviderResult> RequestFullRefundAsync(FullRefundCommand command, CancellationToken ct);
}
