using Avalon.Common.ValueObjects;

namespace Avalon.Domain.Auth;

/// <summary>Only the current challenge per account; never stores the secret sent by email.</summary>
public sealed class AccountEmailVerification
{
    public AccountId AccountId { get; set; }
    public required string TokenHash { get; set; }
    public required string Email { get; set; }
    public int CredentialsVersion { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime? InvalidatedAt { get; set; }
}
