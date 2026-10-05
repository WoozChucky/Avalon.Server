namespace Avalon.Common.GameAuth;

public enum LicenseAuthorityKind { StoredGrant, VerifiedOwnership }

public sealed class LicenseAuthorityDecision
{
    public LicenseAuthorityDecision(bool OwnsProduct, DateTime ObservedAt, DateTime AuthorizedUntil,
        DateTime? ProviderExpiresAt = null, bool Reestablish = false)
    {
        this.OwnsProduct = OwnsProduct;
        this.ObservedAt = ObservedAt;
        this.AuthorizedUntil = AuthorizedUntil;
        this.ProviderExpiresAt = ProviderExpiresAt;
        this.Reestablish = Reestablish;
    }

    public bool OwnsProduct { get; }
    public DateTime ObservedAt { get; }
    public DateTime AuthorizedUntil { get; }
    public DateTime? ProviderExpiresAt { get; }
    public bool Reestablish { get; }
}
