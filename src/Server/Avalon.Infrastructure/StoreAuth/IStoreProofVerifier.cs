namespace Avalon.Infrastructure.StoreAuth;

public enum SteamProofStatus { Verified, InvalidProof, ProviderUnavailable }
public sealed record SteamProofResult(SteamProofStatus Status, string? ProviderSubject = null);

public interface ISteamProofVerifier
{
    Task<SteamProofResult> VerifyAsync(uint appId, string ticketHex, string expectedIdentity, CancellationToken cancellationToken);
}

public enum SteamOwnershipStatus { Owned, NotOwned, ProviderUnavailable }
public sealed record SteamOwnershipResult(SteamOwnershipStatus Status, string ProviderSubject, DateTime ObservedAt,
    DateTime AuthorizedUntil, DateTime? ProviderExpiresAt = null, string? OwnerSubject = null, bool? Permanent = null);

public interface ISteamOwnershipClient
{
    Task<SteamOwnershipResult> CheckAsync(uint appId, string verifiedSteamId, CancellationToken cancellationToken);
}
