namespace Avalon.Domain.Commerce;

public sealed class PaymentDispute
{
    public Guid Id { get; set; }
    public Guid PaymentAttemptId { get; set; }
    public required string Provider { get; set; }
    public required string ProviderAccountId { get; set; }
    public required string Environment { get; set; }
    public required string ExternalReference { get; set; }
    public PaymentDisputeState State { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ObservedAt { get; set; }
    public long Version { get; set; } = 1;
}
