namespace Avalon.Api.Hosting.Config;

/// <summary>
/// The part of <c>Application:Authentication</c> every API service reads to validate an access token (#794): the key
/// that signs it, its issuer and audience, and the clock skew its lifetime is checked with. The service that mints
/// tokens reads the rest of the section as well (lifetimes, the refresh cookie, the login limits).
/// </summary>
public class TokenValidationConfig
{
    public const string Section = "Application:Authentication";

    public string IssuerSigningKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public bool ValidateIssuer { get; set; }
    public string Audience { get; set; } = string.Empty;
    public bool ValidateAudience { get; set; }
    public int ClockSkewInMinutes { get; set; }
}
