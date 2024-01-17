using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Avalon.Api.Config;
using Avalon.Database.Auth;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Avalon.Api.Authentication;

public interface IJwtUtils
{
    string GenerateJwtToken(Account account);
    int? ValidateJwtToken(string? token);
}

public class JwtUtils : IJwtUtils
{
    private readonly JwtSecurityTokenHandler _tokenHandler;
    private readonly AuthenticationConfig _authenticationConfig;
    private readonly SymmetricSecurityKey _key;

    public JwtUtils(AuthenticationConfig authenticationConfig)
    {
        _authenticationConfig = authenticationConfig;
        _tokenHandler = new JwtSecurityTokenHandler();
        _key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(_authenticationConfig.IssuerSigningKey));
    }

    public string GenerateJwtToken(Account account)
    {
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
                {
                    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()), 
                    new Claim(ClaimTypes.NameIdentifier, account.Id.ToString() ?? throw new InvalidOperationException()), 
                    new Claim(JwtRegisteredClaimNames.Name, account.Username),
                    new Claim(JwtRegisteredClaimNames.Email, account.Email),
                    new Claim(ClaimTypes.GroupSid, account.Role),
                },
            JwtBearerDefaults.AuthenticationScheme
            ),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256Signature),
            Issuer = _authenticationConfig.Issuer,
            Audience = _authenticationConfig.Audience,
            IssuedAt = DateTime.UtcNow
        };
        
        var token = _tokenHandler.CreateToken(tokenDescriptor);
        return _tokenHandler.WriteToken(token);
    }

    public int? ValidateJwtToken(string? token)
    {
        if (token == null)
            return null;
        
        try
        {
            _tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidIssuer = _authenticationConfig.Issuer,
                ValidateIssuer = _authenticationConfig.ValidateIssuer,
                IssuerSigningKey = _key,
                ValidateIssuerSigningKey = _authenticationConfig.ValidateIssuerKey,
                ValidAudience = _authenticationConfig.Audience,
                ValidateAudience = _authenticationConfig.ValidateAudience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            }, out var validatedToken);

            var jwtToken = (JwtSecurityToken) validatedToken;
            var accountId = int.Parse(jwtToken.Claims.First(x => x.Type == JwtRegisteredClaimNames.Sub).Value);

            // return account id from JWT token if validation successful
            return accountId;
        }
        catch
        {
            // return null if validation fails
            return null;
        }
    }
}
