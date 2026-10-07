namespace Avalon.Common.GameAuth;

public enum LicenseAuthorityKind { StoredGrant, VerifiedOwnership }

public sealed class LicenseAuthorityDecision
{
    public LicenseAuthorityDecision(bool ownsProduct, DateTime observedAt, DateTime authorizedUntil,
        DateTime? providerExpiresAt = null, bool reestablish = false)
    {
        OwnsProduct = ownsProduct;
        ObservedAt = observedAt;
        AuthorizedUntil = authorizedUntil;
        ProviderExpiresAt = providerExpiresAt;
        Reestablish = reestablish;
    }

    public bool OwnsProduct { get; }
    public DateTime ObservedAt { get; }
    public DateTime AuthorizedUntil { get; }
    public DateTime? ProviderExpiresAt { get; }
    public bool Reestablish { get; }
}
