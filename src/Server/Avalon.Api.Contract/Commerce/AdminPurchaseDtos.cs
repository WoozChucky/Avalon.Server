using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Avalon.Api.Contract.Commerce;

public sealed class PurchaseSearch
{
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 25;
    [Range(1, long.MaxValue)] public long? AccountId { get; set; }
    [RegularExpression("^(pending|fulfilled|reversed|needs-review)$")] public string? State { get; set; }
    [StringLength(32)] public string? Environment { get; set; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class FullRefundRequest
{
    public Guid PaymentAttemptId { get; set; }
    [Required, StringLength(500, MinimumLength = 1)] public string Reason { get; set; } = string.Empty;
}
public sealed record AdminPurchaseDto(Guid OrderId, long AccountId, long OriginalPurchaserAccountId, string Product, string Provider,
    string PaymentEnvironment, string LicenseEnvironment, long AmountMinor, string Currency, long? TaxMinor, string State,
    Guid? FundingAttemptId, Guid? LicenseId, string? Issue, DateTime CreatedAt);
public sealed record AdminPaymentAttemptDto(Guid Id, int Sequence, string State, bool Funding, string? CheckoutReference, string? PaymentReference,
    DateTime CreatedAt, DateTime? LastReconciledAt);
public sealed record AdminRefundDto(Guid Id, Guid PaymentAttemptId, string State, bool Unresolved, long AmountMinor, long? RequestedBy,
    string Reason, string? ExternalReference, string? FailureCode, DateTime RequestedAt);
public sealed record AdminDisputeDto(Guid Id, Guid PaymentAttemptId, string State, string ExternalReference, DateTime ObservedAt);
public sealed record AdminPaymentEventDto(Guid Id, string State, int RetryCount, string? FailureCode, DateTime NextAttemptAt);
public sealed record AdminPurchaseDetailDto(AdminPurchaseDto Purchase, string BeneficiaryUsername, string PurchaserUsername, string LicenseState,
    IReadOnlyList<AdminPaymentAttemptDto> Attempts, IReadOnlyList<AdminRefundDto> Refunds, IReadOnlyList<AdminDisputeDto> Disputes, IReadOnlyList<AdminPaymentEventDto> Events);
public sealed record RefundReply(Guid RefundId, string State, bool NeedsReview);
