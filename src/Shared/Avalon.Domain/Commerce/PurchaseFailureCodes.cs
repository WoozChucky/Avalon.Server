namespace Avalon.Domain.Commerce;

/// <summary>Provider-neutral failures shared by reservation, API and clients.</summary>
public static class PurchaseFailureCodes
{
    public const string AccountUnavailable = "ACCOUNT_UNAVAILABLE";
    public const string EmailNotVerified = "EMAIL_NOT_VERIFIED";
    public const string LicenseAlreadyOwned = "LICENSE_ALREADY_OWNED";
    public const string NeedsReview = "PURCHASE_NEEDS_REVIEW";
    public const string Disabled = "PURCHASE_DISABLED";
    public const string NotFound = "PURCHASE_NOT_FOUND";
    public const string ProviderUnavailable = "PAYMENT_PROVIDER_UNAVAILABLE";
    public const string TooManyAttempts = "TOO_MANY_PURCHASE_ATTEMPTS";
    public const string InvalidRequest = "INVALID_PURCHASE_REQUEST";
}
