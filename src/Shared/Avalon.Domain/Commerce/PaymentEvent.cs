namespace Avalon.Domain.Commerce;

/// <summary>Minimal verified notification inbox; raw bodies and signature proofs are never stored.</summary>
public sealed class PaymentEvent
{
    public Guid Id { get; set; }
    public required string Provider { get; set; }
    public required string ProviderAccountId { get; set; }
    public required string Environment { get; set; }
    public required string ExternalReference { get; set; }
    public required string Type { get; set; }
    public required string ResourceReference { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? PaymentAttemptId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public int RetryCount { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTime? LeaseUntil { get; set; }
    public PaymentEventState State { get; set; }
    public string? FailureCode { get; set; }
    public long Version { get; set; } = 1;
}
