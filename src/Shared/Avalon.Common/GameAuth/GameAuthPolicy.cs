namespace Avalon.Common.GameAuth;

/// <summary>Approved authority limits, independent of deployment. Changing them requires a protocol/security review.</summary>
public static class GameAuthPolicy
{
    public static readonly TimeSpan AttemptLifetime = TimeSpan.FromSeconds(120);
    public static readonly TimeSpan CredentialLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan OwnershipLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan IdentityLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan AbsoluteContextLifetime = TimeSpan.FromHours(12);
    public static readonly TimeSpan LinkProofLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan LinkConsentLifetime = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan JoinTicketLifetime = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan JoinReceiptRetention = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan RefreshReceiptLifetime = TimeSpan.FromSeconds(30);
    /// <summary>How long a refresh receipt is kept from each answer that met a license outage, within the context's absolute expiry.</summary>
    public static readonly TimeSpan OutageReceiptLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MutationClaimLifetime = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan SessionLeaseLifetime = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan SaveDrainMargin = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan AdmissionTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan TransportTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan WebLinkLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan WebProofLifetime = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan WebConfirmationLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan OpenIdNonceLifetime = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan OpenIdClockSkew = TimeSpan.FromSeconds(30);
    public const int MutationAttempts = 3;
    public const int TokenBytes = 32;
    public const int TokenCharacters = 43; // Unpadded base64url of TokenBytes.
    public const int MaximumPkceVerifierCharacters = 128;
    public const int MaximumSteamTicketBytes = 2560;
    public const int MaximumSteamTicketHexCharacters = MaximumSteamTicketBytes * 2;
    public const int MaximumBodyBytes = 16 * 1024;
    public const int MaximumControlBodyBytes = 4 * 1024;
    public const int MaximumProtocolVersionCharacters = 32;
    public const int MaximumJsonDepth = 16;
    public const string ContextAudience = "avalon.game-auth";
}
