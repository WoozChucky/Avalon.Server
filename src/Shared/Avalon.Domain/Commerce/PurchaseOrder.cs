using Avalon.Common.ValueObjects;

namespace Avalon.Domain.Commerce;

/// <summary>Trusted purchase beneficiary and financial provenance, independent of payment provider.</summary>
public sealed class PurchaseOrder
{
    public Guid Id { get; set; }
    public AccountId AccountId { get; set; } = null!;
    public AccountId OriginalPurchaserAccountId { get; set; } = null!;
    public required string Product { get; set; }
    public required string OfferId { get; set; }
    public long AmountMinor { get; set; }
    public required string Currency { get; set; }
    public required string Provider { get; set; }
    public required string ProviderAccountId { get; set; }
    public required string PaymentEnvironment { get; set; }
    public required string LicenseEnvironment { get; set; }
    public bool Unresolved { get; set; } = true;
    public Guid? FundingAttemptId { get; set; }
    public Guid? LicenseId { get; set; }
    public long? TaxMinor { get; set; }
    public long? SubtotalMinor { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? FulfilledAt { get; set; }
    public DateTime? ReversedAt { get; set; }
    public string? ReconciliationIssue { get; set; }
    public long Version { get; set; } = 1;
}
