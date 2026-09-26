using System.Security.Claims;
using Avalon.Api.Authentication;
using Avalon.Api.Authentication.AV;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using Avalon.Api.Services;
using Avalon.Database.Auth.Extensions;
using Avalon.Infrastructure.Extensions;
using Avalon.Database.Character.Extensions;
using Avalon.Database.World.Extensions;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Configuration;
using Avalon.Infrastructure.Login;
using Avalon.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;

namespace Avalon.Api;

public static class ServiceRegistration
{
    public static void AddInfrastructure(this IServiceCollection services, ApplicationConfig config)
    {
        services.AddAuthDatabase();
        services.AddCharacterDatabase();
        services.AddWorldDatabase();

        services.AddOptions<CacheConfiguration>()
            .BindConfiguration("Application:Cache");

        services.AddOptions<MapAssetConfig>()
            .BindConfiguration("Application:MapAssets");

        services.AddMemoryCache();

        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ICharacterService, CharacterService>();
        services.AddScoped<IWorldService, WorldService>();
        services.AddScoped<IMapService, MapService>();
        services.AddScoped<IProceduralLayoutInputsResolver, ProceduralLayoutInputsResolver>();
        services.AddScoped<IObservabilityService, ObservabilityService>();
        services.AddScoped<IPersonalAccessTokenService, PersonalAccessTokenService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddSingleton(TimeProvider.System);
        services.AddMfaService();
        // The login policy the Auth server shares (#478), with the limits under
        // Application:Authentication, checked here since that section is bound without validation.
        LoginLimitsValidation.Validate(config.Authentication ?? new AuthenticationConfig(), "Application:Authentication");
        services.AddSingleton<ILoginLimits>(sp => sp.GetRequiredService<AuthenticationConfig>());
        services.AddLoginPolicy();
        services.AddScoped<IReauthentication, Reauthentication>();
        services.AddSecureRandom();
        services.AddSingleton<IReplicatedCache, ReplicatedCache>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IJwtUtils, JwtUtils>();
    }

    public static void AddAuth(this IServiceCollection services, ApplicationConfig config)
    {
        // Checked here, eagerly, so a missing or weak key stops startup instead of surfacing on the
        // first request (#482).
        // One instance, registered for JwtUtils, so signing and validation share it.
        SymmetricSecurityKey signingKey = JwtSigningKey.Create(config.Authentication);
        services.AddSingleton(signingKey);

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
                    OnMessageReceived = context =>
                    {
                        if (context.Request.Headers.TryGetValue(HeaderNames.Authorization, out StringValues authHeader))
                        {
                            var value = authHeader.ToString();
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
                    },
                    // A valid signature is not enough: the account behind the token is reloaded
                    // and re-checked on every request, as a PAT's is (#480).
                    OnTokenValidated = JwtAccountRevalidation.OnTokenValidated,
                };

                x.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = config.Authentication!.Issuer,
                    ValidateIssuer = config.Authentication.ValidateIssuer,
                    IssuerSigningKey = signingKey,
                    // Not configurable: a token is only as good as the key that signed it.
                    ValidateIssuerSigningKey = true,
                    ValidAudience = config.Authentication.Audience,
                    ValidateAudience = config.Authentication.ValidateAudience,
                    // The access token's lifetime (AccessTokenLifetimeMinutes) is enforced, with the
                    // configured skew; a client past it gets a 401 and refreshes (#480).
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(config.Authentication.ClockSkewInMinutes),
                    RoleClaimType = ClaimTypes.GroupSid,
                    // JwtUtils signs with HMAC-SHA256 only; nothing else is accepted.
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                };

                x.Validate(JwtBearerDefaults.AuthenticationScheme);
            })
            .AddScheme<AvalonAuthenticationSchemeOptions, AvalonAuthenticationHandler>(
                AvalonAuthenticationSchemeOptions.SchemeName,
                AvalonAuthenticationSchemeOptions.SchemeName,
                options => { }
            );

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

        services.AddScoped<IAuthContext, AuthContext>();
        services.AddScoped<IAuthorizationHandler, AvalonAuthHandler>();
        services.AddScoped<IAuthorizationHandler, Authorization.CharacterReadHandler>();
        services.AddScoped<IAuthorizationHandler, Authorization.CharacterWriteHandler>();
        services.AddScoped<IAuthorizationHandler, Authorization.AccountReadHandler>();
        services.AddScoped<IAuthorizationHandler, Authorization.AccountWriteHandler>();
        services.AddScoped<IAuthorizationHandler, Authorization.PatReadHandler>();
        services.AddScoped<IAuthorizationHandler, Authorization.PatWriteHandler>();
    }
}
