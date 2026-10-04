using Avalon.Common.ValueObjects;

namespace Avalon.Domain.Auth;

/// <summary>Durable creation receipt, retained independently of the account so a deleted root cannot be resurrected by a retry.</summary>
public sealed class StoreAccountCreation
{
    public Guid Id { get; init; }
    public AccountId AccountId { get; init; }
    public required string ProviderSubject { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime ProofExpiresAt { get; init; }
}
