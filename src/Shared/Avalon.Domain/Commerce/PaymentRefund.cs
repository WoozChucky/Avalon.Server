using Avalon.Common.ValueObjects;

namespace Avalon.Domain.Commerce;

public sealed class PaymentRefund
{
    public Guid Id { get; set; }
    public Guid PaymentAttemptId { get; set; }
    public required string Provider { get; set; }
    public required string ProviderAccountId { get; set; }
    public required string Environment { get; set; }
    public required string OperationKey { get; set; }
    public AccountId RequestedBy { get; set; } = null!;
    public required string Reason { get; set; }
    public long AmountMinor { get; set; }
    public string? ExternalReference { get; set; }
    public PaymentRefundState State { get; set; }
    public bool Unresolved { get; set; } = true;
    public DateTime RequestedAt { get; set; }
    public DateTime? FirstDispatchedAt { get; set; }
    public DateTime? ReplayDeadline { get; set; }
    public DateTime? ObservedAt { get; set; }
    public string? FailureCode { get; set; }
    public long Version { get; set; } = 1;
}
