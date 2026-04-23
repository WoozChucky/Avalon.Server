namespace Avalon.Api.Config;

public class AuthenticationConfig
{
    public string IssuerSigningKey { get; set; } = string.Empty;
    public bool ValidateIssuerKey { get; set; }
    public string Issuer { get; set; } = string.Empty;
    public bool ValidateIssuer { get; set; }
    public string Audience { get; set; } = string.Empty;
    public bool ValidateAudience { get; set; }
    public int ClockSkewInMinutes { get; set; }
    public int AccessTokenLifetimeMinutes { get; set; } = 15;
    public int RefreshTokenLifetimeDays { get; set; } = 30;
    public string RefreshCookieName { get; set; } = "av_refresh";
    // Cookie path is "/" so the browser attaches the refresh cookie regardless of
    // any proxy path rewriting (e.g. Vite dev proxy strips /api before forwarding,
    // producing a Set-Cookie path that the browser can't match on subsequent
    // requests). Defense-in-depth via HttpOnly + SameSite=Strict + Secure remains.
    public string RefreshCookiePath { get; set; } = "/";
}
