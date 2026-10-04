using Avalon.Common.ValueObjects;

namespace Avalon.Domain.Characters;

public enum GameplayFenceMode { Blocked, Pending, Active, Draining }

/// <summary>One durable writer guard per account in each world's Character database.</summary>
public sealed class AccountGameplayFence
{
    public AccountId AccountId { get; set; } = null!;
    public Guid GameSessionId { get; set; }
    public long FencingToken { get; set; }
    public GameplayFenceMode Mode { get; set; }
    public DateTime LeaseUntil { get; set; }
    public Guid? ConsolidationId { get; set; }
}
