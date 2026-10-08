using Avalon.Api.Hosting.Authentication.Jwt;

namespace Avalon.Api.Hosting.Config;

/// <summary>
/// The part of <c>Application:Authentication</c> every API service reads to validate an access token (#794): the keys
/// that sign it (<see cref="JwtKeys"/>), its issuer and audience, and the clock skew its lifetime is checked with. The
/// service that mints tokens reads the rest of the section as well (lifetimes, the refresh cookie, the login limits).
/// </summary>
public class TokenValidationConfig
{
    public const string Section = "Application:Authentication";

    /// <summary>
    /// The ES256 private key identity signs access tokens with (#801): an EC P-256 key in PKCS#8, as PEM or as the base64
    /// of its DER. Read here so that a process that does not sign can refuse it: only identity may hold it, since a key
    /// that signs could mint an Admin token.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>The key id (<c>kid</c>) identity writes into the header of the tokens it signs with <see cref="SigningKey"/>.</summary>
    public string SigningKeyId { get; set; } = string.Empty;

    /// <summary>
    /// The public keys an access token may be signed with, by key id: each an EC P-256 SubjectPublicKeyInfo, as the
    /// base64 of its DER or as PEM. Public, not secret. A process that signs accepts its own key without it listed.
    /// </summary>
    public Dictionary<string, string> ValidationKeys { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The setting of the HS256 key access tokens were signed with before #801. Nothing reads its value: HS256 is refused
    /// whatever is configured. A process that still has it set warns once at startup, naming it, so it can be removed.
    /// </summary>
    public const string IssuerSigningKeySetting = Section + ":IssuerSigningKey";

    public string Issuer { get; set; } = string.Empty;
    public bool ValidateIssuer { get; set; }
    public string Audience { get; set; } = string.Empty;
    public bool ValidateAudience { get; set; }
    public int ClockSkewInMinutes { get; set; }
}
