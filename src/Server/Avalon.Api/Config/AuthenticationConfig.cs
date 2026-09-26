using Avalon.Infrastructure.Login;

namespace Avalon.Api.Config;

public class AuthenticationConfig : ILoginLimits
{
    public string IssuerSigningKey { get; set; } = string.Empty;
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

    // The login limits (#478). The budgets they govern are the Redis keys the Auth server uses
    // too, so these must match the Auth server's Application settings of the same names; the
    // defaults do.

    /// <inheritdoc/>
    public int MaxFailedLoginAttempts { get; set; } = 5;

    /// <inheritdoc/>
    public int LockoutDurationMinutes { get; set; } = 15;

    /// <inheritdoc/>
    public int MaxFailedLoginsPerSource { get; set; } = 10;

    /// <inheritdoc/>
    public int FailedLoginSourceWindowMinutes { get; set; } = 15;

    /// <inheritdoc/>
    public int MaxFailedMfaAttempts { get; set; } = 5;

    /// <summary>
    /// Accounts one source (an IPv4 address, or an IPv6 /64) may create per
    /// <see cref="AccountCreationWindowMinutes"/> (#495 review). A created account is never given
    /// back, unlike a login's slot, so the cap counts accounts, not attempts. API only.
    /// </summary>
    public int MaxAccountsCreatedPerSource { get; set; } = 5;

    /// <summary>The window, fixed from a source's first creation, over which its creations are counted.</summary>
    public int AccountCreationWindowMinutes { get; set; } = 60;

    /// <summary>
    /// Email-change confirmations one account may have sent per <see cref="EmailChangeSendWindowMinutes"/>
    /// (#510 review). A send is never given back, so a player holding the password still cannot use
    /// the api to mail arbitrary addresses. API only.
    /// </summary>
    public int MaxEmailChangeSendsPerAccount { get; set; } = 3;

    /// <summary>Email-change confirmations sent to one address, from every account, per <see cref="EmailChangeSendWindowMinutes"/>.</summary>
    public int MaxEmailChangeSendsPerAddress { get; set; } = 3;

    /// <summary>The window, fixed from the first send, over which both send budgets count.</summary>
    public int EmailChangeSendWindowMinutes { get; set; } = 60;
}
