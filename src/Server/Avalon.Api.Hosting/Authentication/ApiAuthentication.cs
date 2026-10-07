using System.Security.Claims;
using Avalon.Api.Hosting.Authentication.AV;
using Avalon.Api.Hosting.Authentication.Jwt;
using Avalon.Api.Hosting.Config;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;

namespace Avalon.Api.Hosting.Authentication;

/// <summary>
/// The credentials every API service accepts (#794, design section 4.1): the access JWT, from the
/// <c>Authorization: Bearer</c> header or the <see cref="AuthConstants.CookieName"/> cookie, revalidated against its
/// account on every request (<see cref="JwtAccountRevalidation"/>), and the personal access token
/// (<see cref="AvalonAuthenticationHandler"/>); the default policy and the role policies; and the default policy's
/// handler. A service adds its own schemes, policies and resource handlers on top.
/// </summary>
public static class ApiAuthentication
{
    /// <summary>
    /// Registers the signing key, the two schemes, the policies and <see cref="AvalonAuthHandler"/>. The key is checked
    /// here, eagerly, so a missing or weak one stops startup naming the setting instead of surfacing on the first
    /// request (#482); the one instance is registered, so the service that mints tokens signs with the key they are
    /// validated with.
    /// </summary>
    public static AuthenticationBuilder AddApiAuthentication(this IServiceCollection services, TokenValidationConfig? config)
    {
        SymmetricSecurityKey signingKey = JwtSigningKey.Create(config);
        services.AddSingleton(signingKey);
        AuthenticationBuilder authentication = AddAuthenticationSchemes(services, config!, signingKey);
        AddAuthorizationPolicies(services);
        services.AddScoped<IAuthContext, AuthContext>();
        services.AddScoped<IAuthorizationHandler, AvalonAuthHandler>();
        return authentication;
    }

    private static AuthenticationBuilder AddAuthenticationSchemes(IServiceCollection services, TokenValidationConfig config,
        SymmetricSecurityKey signingKey) =>
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            }).AddJwtBearer(x =>
            {
                x.SaveToken = true;
                x.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ReadAccessToken,
                    // A valid signature is not enough: the account behind the token is reloaded
                    // and re-checked on every request, as a PAT's is (#480).
                    OnTokenValidated = JwtAccountRevalidation.OnTokenValidated,
                };

                x.TokenValidationParameters = BuildTokenValidationParameters(config, signingKey);

                x.Validate(JwtBearerDefaults.AuthenticationScheme);
            })
            .AddScheme<AvalonAuthenticationSchemeOptions, AvalonAuthenticationHandler>(
                AvalonAuthenticationSchemeOptions.SchemeName,
                AvalonAuthenticationSchemeOptions.SchemeName,
                options => { }
            );

    private static Task ReadAccessToken(MessageReceivedContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderNames.Authorization, out StringValues authHeader))
        {
            string value = authHeader.ToString();
            // Only extract the token when the scheme is Bearer (JWT).
            // Avalon-scheme headers (PATs) are handled by AvalonAuthenticationHandler;
            // passing them to JwtBearer causes a Fail() result and a spurious 401.
            if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                context.Token = value["Bearer ".Length..];
            }
        }
        else if (context.Request.Cookies.TryGetValue(AuthConstants.CookieName, out string? cookie))
        {
            context.Token = cookie;
        }

        return Task.CompletedTask;
    }

    private static TokenValidationParameters BuildTokenValidationParameters(TokenValidationConfig config,
        SymmetricSecurityKey signingKey) =>
        new()
        {
            ValidIssuer = config.Issuer,
            ValidateIssuer = config.ValidateIssuer,
            IssuerSigningKey = signingKey,
            // Not configurable: a token is only as good as the key that signed it.
            ValidateIssuerSigningKey = true,
            ValidAudience = config.Audience,
            ValidateAudience = config.ValidateAudience,
            // The access token's lifetime (AccessTokenLifetimeMinutes) is enforced, with the
            // configured skew; a client past it gets a 401 and refreshes (#480).
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(config.ClockSkewInMinutes),
            RoleClaimType = ClaimTypes.GroupSid,
            // Tokens are signed with HMAC-SHA256 only; nothing else is accepted.
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        };

    private static void AddAuthorizationPolicies(IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.DefaultPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme,
                    AvalonAuthenticationSchemeOptions.SchemeName)
                .RequireAuthenticatedUser()
                .AddRequirements(new AvalonAuthRequirement())
                .Build();

            options.AddPolicy(AvalonRoles.Console, policy => policy
                .RequireClaim(ClaimTypes.GroupSid, AvalonRoles.Console)
                .Combine(options.DefaultPolicy)
            );

            options.AddPolicy(AvalonRoles.Admin, policy => policy
                .RequireClaim(ClaimTypes.GroupSid, AvalonRoles.Admin, AvalonRoles.Console)
                .Combine(options.DefaultPolicy)
            );

            options.AddPolicy(AvalonRoles.GameMaster, policy => policy
                .RequireClaim(ClaimTypes.GroupSid, AvalonRoles.GameMaster, AvalonRoles.Admin, AvalonRoles.Console)
                .Combine(options.DefaultPolicy)
            );

            options.AddPolicy(AvalonRoles.Player, policy => policy
                .RequireClaim(ClaimTypes.GroupSid, AvalonRoles.Player, AvalonRoles.Tournament, AvalonRoles.PTR,
                    AvalonRoles.GameMaster, AvalonRoles.Admin, AvalonRoles.Console)
                .Combine(options.DefaultPolicy)
            );
        });
    }
}
