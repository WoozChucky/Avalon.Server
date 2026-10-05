using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;

namespace Avalon.Domain.Auth;

/// <summary>Shared source-bound license. Store records require current verified authority, not just this row.</summary>
public sealed class GameLicense
{
    public Guid Id { get; set; }
    public AccountId AccountId { get; set; } = null!;
    public required string Product { get; set; }
    public required string Provider { get; set; }
    public required string Environment { get; set; }
    public required string ProviderProductId { get; set; }
    public string? ProviderSubject { get; set; }
    public required string LicenseReference { get; set; }
    public LicenseAuthorityKind AuthorityKind { get; set; }
    public DateTime GrantedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public long AuthorityRevision { get; set; } = 1;
    public DateTime? LastObservedAt { get; set; }
    public DateTime? VerifiedUntil { get; set; }

    public bool Authorizes(AccountId account, string product, string environment, DateTime now) =>
        AccountId == account && Product == product && Environment == environment && AuthorityRevision > 0 &&
        GrantedAt <= now && RevokedAt is null && (ExpiresAt is null || ExpiresAt > now) &&
        (AuthorityKind == LicenseAuthorityKind.StoredGrant ||
         (AuthorityKind == LicenseAuthorityKind.VerifiedOwnership && LastObservedAt <= now &&
          VerifiedUntil > now && VerifiedUntil <= LastObservedAt.Value.AddMinutes(5)));
}
