namespace Avalon.Api.Config;

/// <summary>
/// Request rate limiting (#561), bound from <c>Application:RateLimiting</c>: a sliding window of
/// one minute per caller, the account for an authenticated request and the source address for
/// any other. In memory, since the api runs as one replica.
/// </summary>
public class RateLimitingConfig
{
    /// <summary>When false, nothing is limited.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Requests a minute per source (IPv4 address or IPv6 /64) for a caller that is not signed in,
    /// or whose token is not valid. At least 1.
    /// </summary>
    public int AnonymousPermitsPerMinute { get; set; } = 60;

    /// <summary>Requests a minute per account for a caller with a valid access token or personal access token. At least 1.</summary>
    public int AuthenticatedPermitsPerMinute { get; set; } = 300;

    /// <summary>
    /// Launcher sign-in requests (<c>client/auth</c>: code, token, refresh, revoke) a minute per source,
    /// on top of the limits above (#591). At least 1.
    /// </summary>
    public int ClientAuthPermitsPerMinute { get; set; } = 20;
}
