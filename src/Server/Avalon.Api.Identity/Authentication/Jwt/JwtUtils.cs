using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Avalon.Api.Hosting.Authentication;
using Avalon.Api.Hosting.Authentication.Jwt;
using Avalon.Api.Identity.Config;
using Avalon.Common.Accounts;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Avalon.Api.Identity.Authentication.Jwt;

public interface IJwtUtils
{
    string GenerateJwtToken(Account account);
    string GenerateLauncherJwtToken(Account account, Guid familyId);
}

public class JwtUtils : IJwtUtils
{
    /// <summary>
    /// The claim holding the account's credentials version when the token was minted (#495). The
    /// token is refused once the account's version has moved on.
    /// </summary>
    public const string CredentialsVersionClaim = JwtClaims.CredentialsVersion;
    public const string LauncherFamilyClaim = "launcher_family";

    private readonly JwtSecurityTokenHandler _tokenHandler;
    private readonly AuthenticationConfig _authenticationConfig;
    private readonly SigningCredentials _signing;

    /// <param name="authenticationConfig">Issuer, audience and lifetime of the tokens.</param>
    /// <param name="keys">
    /// The singleton <see cref="ApiAuthentication.AddApiAuthentication"/> registers: identity's private key, which signs
    /// with ES256 under its key id (#801), and the public keys the bearer handler validates with.
    /// </param>
    public JwtUtils(AuthenticationConfig authenticationConfig, JwtKeys keys)
    {
        _authenticationConfig = authenticationConfig;
        _tokenHandler = new JwtSecurityTokenHandler();
        // Never null where identity runs: JwtKeys.Create refuses to start a process that signs without the key.
        _signing = keys.Signing ?? throw new InvalidOperationException(
            $"This process has no key to sign access tokens with ({JwtKeys.SigningKeySetting}); only identity mints them.");
    }

    public string GenerateJwtToken(Account account) => Generate(account, null);

    public string GenerateLauncherJwtToken(Account account, Guid familyId) => Generate(account, familyId);

    private string Generate(Account account, Guid? launcherFamilyId)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, account.Id.ToString() ?? throw new InvalidOperationException()),
            new(JwtRegisteredClaimNames.Name, account.Username),
        };

        // A Steam-only account has no email until it adds recovery credentials: its token carries no email claim.
        if (account.Email is { } email)
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, email));

        claims.Add(new Claim(CredentialsVersionClaim,
            account.CredentialsVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ClaimValueTypes.Integer32));

        if (launcherFamilyId is { } familyId)
            claims.Add(new Claim(LauncherFamilyClaim, familyId.ToString()));

        // Emit one GroupSid claim per individual flag bit that is set
        foreach (AccountAccessLevel flag in Enum.GetValues<AccountAccessLevel>())
        {
            if (flag == 0) continue; // skip 'None' if present
            if (account.AccessLevel.HasFlag(flag))
                claims.Add(new Claim(ClaimTypes.GroupSid, flag.ToString()));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims, JwtBearerDefaults.AuthenticationScheme),
            Expires = DateTime.UtcNow.AddMinutes(_authenticationConfig.AccessTokenLifetimeMinutes),
            // ES256 with the key id in the header (#801); the payload is what the HS256 tokens carried.
            SigningCredentials = _signing,
            Issuer = _authenticationConfig.Issuer,
            Audience = _authenticationConfig.Audience,
            IssuedAt = DateTime.UtcNow
        };

        SecurityToken token = _tokenHandler.CreateToken(tokenDescriptor);
        return _tokenHandler.WriteToken(token);
    }
}
