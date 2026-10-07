using Avalon.Api.Services;

// The published OpenAPI document names this schema by the type's full name, so the type keeps the namespace it had
// before the split (#794).
namespace Avalon.Api.Controllers;

public sealed record SteamWebLinkReply(string State, string? Error = null)
{
    public string? TransactionId { get; init; }
    public string? ConfirmationId { get; init; }
    public string? ChallengeUrl { get; init; }
    public bool RequiresConsolidation { get; init; }
    public bool RequiresMfa { get; init; }
    public string? Username { get; init; }
    public string? SteamId { get; init; }
    public DateTime? ProofExpiresAt { get; init; }
    public AccountConsolidationReply? Consolidation { get; init; }
}
public sealed record SteamWebLinkConfirmation(Guid TransactionId, string CurrentPassword, string? MfaCode, bool Confirmed,
    bool ConfirmedConsolidation = false);
