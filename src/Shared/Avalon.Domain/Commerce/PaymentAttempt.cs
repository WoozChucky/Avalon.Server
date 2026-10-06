namespace Avalon.Domain.Commerce;

/// <summary>Immutable checkout request parameters and durable provider operation identity.</summary>
public sealed class PaymentAttempt
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public int Sequence { get; set; } = 1;
    public required string Provider { get; set; }
    public required string ProviderAccountId { get; set; }
    public required string Environment { get; set; }
    public required string OperationKey { get; set; }
    public required string ProviderPriceId { get; set; }
    public string ProviderCatalogProductId { get; set; } = string.Empty;
    /// <summary>Ordered, comma-separated validated method identifiers; part of the frozen request.</summary>
    public string PaymentMethods { get; set; } = string.Empty;
    public DateTime RequestedExpiresAt { get; set; }
    public required string CheckoutEmail { get; set; }
    public required string SuccessUrl { get; set; }
    public required string CancelUrl { get; set; }
    public string? CheckoutReference { get; set; }
    public string? PaymentReference { get; set; }
    public string? CheckoutUrl { get; set; }
    public PaymentAttemptState State { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? FirstDispatchedAt { get; set; }
    public DateTime? ReplayDeadline { get; set; }
    public DateTime? LastReconciledAt { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTime? LeaseUntil { get; set; }
    public long Version { get; set; } = 1;
}
