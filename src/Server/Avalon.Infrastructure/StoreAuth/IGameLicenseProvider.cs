using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;

namespace Avalon.Infrastructure.StoreAuth;

public sealed record GameLicenseCheckRequest(AccountId Account, GameApplicationSelection Application,
    VerifiedGameIdentity? Identity, Guid? BoundLicenseId, long? BoundRevision, DateTime Now);
public enum GameLicenseCheckStatus { Licensed, Unlicensed, Unavailable }
public sealed record GameLicenseCheckResult(GameLicenseCheckStatus Status, string LicenseReference, DateTime ObservedAt,
    DateTime AuthorizedUntil, DateTime? ProviderExpiresAt = null, Guid? LicenseId = null,
    string? ProviderProductId = null, string? ProviderSubject = null, string? OwnerSubject = null, bool? Permanent = null);

public interface IGameLicenseProvider
{
    string Provider { get; }
    LicenseAuthorityKind AuthorityKind { get; }
    Task<GameLicenseCheckResult> CheckAsync(GameLicenseCheckRequest request, CancellationToken ct);
}
