using Avalon.Api.Hosting.Config;
using Avalon.Api.Hosting.Middlewares;
using Avalon.Api.Hosting.Worlds;
using Avalon.Database;
using Avalon.Database.Auth.Extensions;
using Avalon.Database.Character.Extensions;
using Avalon.Database.Extensions;
using Avalon.Database.World.Extensions;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avalon.Api.Hosting;

/// <summary>
/// What the shared hosting runs on (#794, design section 3.1), for the needs of the services a process runs: the
/// auth database every service validates credentials against, the forwarded headers, the request rate limiter, Redis
/// when a service needs it, the world databases a service reads, and the auth schema gate. The token validation
/// itself is <see cref="Authentication.ApiAuthentication.AddApiAuthentication"/>, which needs the signing key.
/// </summary>
public static class ApiHostingRegistration
{
    public const string CacheSection = "Application:Cache";

    public static IServiceCollection AddApiHosting(this IServiceCollection services, ApiServiceNeeds needs,
        ForwardedHeadersConfig? forwardedHeaders)
    {
        services.TryAddSingleton(TimeProvider.System);

        // The accounts and personal access tokens every request's credential is checked against (#480).
        services.AddAuthDatabase();
        // Checked at startup (ApiStartup), naming the setting, like the auth and world servers do.
        services.ValidateDatabasesOnStart(DatabaseConnections.Auth);

        if (needs.WorldDatabases != WorldDatabaseParts.None)
        {
            // The repositories only. Their contexts come from AddWorldDatabases: one world and characters
            // database per world under Database:Worlds, chosen per request (#523).
            if (needs.WorldDatabases.HasFlag(WorldDatabaseParts.Characters))
                services.AddCharacterRepositories();
            if (needs.WorldDatabases.HasFlag(WorldDatabaseParts.World))
                services.AddWorldRepositories();
            services.AddWorldDatabases(needs.WorldDatabases);
        }

        if (needs.Redis)
        {
            services.AddOptions<CacheConfiguration>()
                .BindConfiguration(CacheSection)
                .ValidateDataAnnotations()
                .ValidateOnStart();
        }

        // Which proxies' X-Forwarded-For is believed: the caller's address is its login source.
        // Built here so a bad entry stops startup, naming the setting.
        services.AddSingleton(ForwardedHeadersSetup.BuildOptions(forwardedHeaders));
        services.AddSingleton<UntrustedForwardedHeaderLog>();
        // Request rate limiting under Application:RateLimiting (#561), per account or per source.
        services.AddApiRateLimiting();

        if (needs.Redis)
            services.AddSingleton<IReplicatedCache, ReplicatedCache>();

        services.TryAddSingleton(sp => new ApiDatabaseMigrator(sp.GetRequiredService<ILogger<ApiDatabaseMigrator>>()));
        services.AddSingleton(sp => new AuthSchemaGate(needs.AuthSchema,
            AuthSchemaGate.WaitFrom(sp.GetRequiredService<IConfiguration>()), sp.GetRequiredService<TimeProvider>()));
        return services;
    }
}
