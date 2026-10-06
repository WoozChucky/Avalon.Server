namespace Avalon.Domain.Auth;

/// <summary>Independent suspension cause on a shared game license.</summary>
public sealed class LicenseHold
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public required string CauseKind { get; set; }
    public required string CauseReference { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? ReleasedAt { get; set; }
}
