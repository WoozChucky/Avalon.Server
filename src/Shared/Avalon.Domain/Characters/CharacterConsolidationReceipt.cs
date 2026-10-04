using Avalon.Common.ValueObjects;

namespace Avalon.Domain.Characters;

/// <summary>Recover a committed world transfer when publishing Auth-DB progress fails.</summary>
public sealed class CharacterConsolidationReceipt
{
    public Guid Id { get; set; }
    public AccountId SourceAccountId { get; set; } = null!;
    public AccountId TargetAccountId { get; set; } = null!;
    public int TransferredCharacters { get; set; }
    public DateTime TransferredAt { get; set; }
}
