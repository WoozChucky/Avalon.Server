namespace Avalon.Api.Hosting.Authentication.Jwt;

/// <summary>The claims of an access token that its validation reads, beside the standard ones.</summary>
public static class JwtClaims
{
    /// <summary>
    /// The account's credentials version when the token was minted (#495). The token is refused once the account's
    /// version has moved on.
    /// </summary>
    public const string CredentialsVersion = "cver";
}
