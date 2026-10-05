using Avalon.Common.ValueObjects;

namespace Avalon.Domain.Auth;

public enum AccountConsolidationState { Transferring, Finalized, Completed }

/// <summary>Durable consent and progress; separate world databases commit independently.</summary>
public sealed class AccountConsolidation
{
    public Guid Id { get; set; }
    public AccountId SourceAccountId { get; set; } = null!;
    public AccountId TargetAccountId { get; set; } = null!;
    public string Provider { get; set; } = "steam";
    public required string ProviderSubject { get; set; }
    public int TargetCredentialsVersion { get; set; }
    public long TargetSessionEpoch { get; set; }
    public Guid? ConfirmedMfaId { get; set; }
    public DateTime AuthorizedAt { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public AccountConsolidationState State { get; set; }
    public List<AccountConsolidationWorld> Worlds { get; set; } = [];
}

public sealed class AccountConsolidationWorld
{
    public Guid ConsolidationId { get; set; }
    public ushort WorldId { get; set; }
    public int TransferredCharacters { get; set; }
    public DateTime? TransferredAt { get; set; }
    public DateTime? GuardReleasedAt { get; set; }
}
