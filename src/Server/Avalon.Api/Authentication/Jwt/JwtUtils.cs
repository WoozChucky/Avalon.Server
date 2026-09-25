using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Avalon.Api.Config;
using Avalon.Common.Accounts;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Avalon.Api.Authentication.Jwt;

public interface IJwtUtils
{
    string GenerateJwtToken(Account account);
}

public class JwtUtils : IJwtUtils
{
    private readonly JwtSecurityTokenHandler _tokenHandler;
    private readonly AuthenticationConfig _authenticationConfig;
    private readonly SymmetricSecurityKey _key;

    /// <param name="authenticationConfig">Issuer, audience and lifetime of the tokens.</param>
    /// <param name="signingKey">
    /// The singleton <see cref="ServiceRegistration.AddAuth"/> registers from
    /// <see cref="JwtSigningKey.Create(AuthenticationConfig?)"/>: the same instance the bearer handler
    /// validates with.
    /// </param>
    public JwtUtils(AuthenticationConfig authenticationConfig, SymmetricSecurityKey signingKey)
    {
        _authenticationConfig = authenticationConfig;
        _tokenHandler = new JwtSecurityTokenHandler();
        _key = signingKey;
    }

    public string GenerateJwtToken(Account account)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, account.Id.ToString() ?? throw new InvalidOperationException()),
            new(JwtRegisteredClaimNames.Name, account.Username),
            new(JwtRegisteredClaimNames.Email, account.Email),
        };

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
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256Signature),
            Issuer = _authenticationConfig.Issuer,
            Audience = _authenticationConfig.Audience,
            IssuedAt = DateTime.UtcNow
        };

        var token = _tokenHandler.CreateToken(tokenDescriptor);
        return _tokenHandler.WriteToken(token);
    }
}
