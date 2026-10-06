namespace Avalon.Domain.Commerce;

public enum PaymentAttemptState { Reserved, CheckoutOpen, ProviderUnknown, Processing, Paid, Failed, Expired, Canceled, NeedsReview }
public enum PaymentRefundState { Pending, Succeeded, Failed, Canceled, NeedsReview }
public enum PaymentDisputeState { Inquiry, Open, UnderReview, Won, Lost, Accepted }
public enum PaymentEventState { Pending, Processing, Completed, NeedsReview }
