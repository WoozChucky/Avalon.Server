using Avalon.Domain.Commerce;

namespace Avalon.Api.Commerce;

internal static class StripePaymentMapping
{
    public static PaymentRefundState Refund(string status) => status switch
    {
        "pending" or "requires_action" => PaymentRefundState.Pending,
        "succeeded" => PaymentRefundState.Succeeded,
        "failed" => PaymentRefundState.Failed,
        "canceled" => PaymentRefundState.Canceled,
        _ => throw new PaymentProviderException("UNKNOWN_REFUND_STATE"),
    };
    public static PaymentDisputeState Dispute(string status) => status switch
    {
        "warning_needs_response" or "warning_under_review" or "warning_closed" => PaymentDisputeState.Inquiry,
        "needs_response" => PaymentDisputeState.Open,
        "under_review" => PaymentDisputeState.UnderReview,
        "won" => PaymentDisputeState.Won,
        "lost" => PaymentDisputeState.Lost,
        "prevented" => PaymentDisputeState.Won,
        _ => throw new PaymentProviderException("UNKNOWN_DISPUTE_STATE"),
    };
    public static PaymentAttemptState Checkout(string status, string payment) => (status, payment) switch
    {
        ("complete", "paid") => PaymentAttemptState.Paid,
        ("complete", "unpaid") => PaymentAttemptState.Processing,
        ("open", "unpaid") => PaymentAttemptState.CheckoutOpen,
        ("expired", "unpaid") => PaymentAttemptState.Expired,
        _ => throw new PaymentProviderException("UNKNOWN_CHECKOUT_STATE"),
    };
}
