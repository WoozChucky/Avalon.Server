using Avalon.Database.Character;
using Avalon.Database.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Api.Hosting.Worlds;

public static class WorldDatabaseRegistration
{
    /// <summary>
    /// The api's registration (#523): the worlds under Database:Worlds, built from configuration when first resolved.
    /// Deliberately not an options validation: OpenAPI generation starts the host, which runs those, with no world
    /// configured. ApiStartup checks Database:Worlds instead, right after the options, and generation skips
    /// ApiStartup.
    /// </summary>
    public static IServiceCollection AddWorldDatabases(this IServiceCollection services) =>
        services.AddWorldDatabases(WorldDatabaseParts.Both);

    /// <summary>
    /// <see cref="AddWorldDatabases(IServiceCollection)"/> for a process that reads only <paramref name="parts"/> of
    /// each world (#794): only their strings are required.
    /// </summary>
    public static IServiceCollection AddWorldDatabases(this IServiceCollection services, WorldDatabaseParts parts)
    {
        services.AddSingleton(sp => new WorldDatabases(WorldDatabaseSettings.Parse(sp.GetRequiredService<IConfiguration>(), parts)));
        services.AddSingleton<IWorldDatabases>(sp => sp.GetRequiredService<WorldDatabases>());
        // Rechecks a world the startup check found unavailable; idle unless ApiStartup hands it one.
        services.AddSingleton<WorldDatabaseRecheck>();
        services.AddHostedService(sp => sp.GetRequiredService<WorldDatabaseRecheck>());
        return services.AddWorldContexts();
    }

    /// <summary>Tests: the given worlds, with their statuses as the test set them; no startup check.</summary>
    public static IServiceCollection AddWorldDatabases(this IServiceCollection services, WorldDatabases databases)
    {
        services.AddSingleton(databases);
        services.AddSingleton<IWorldDatabases>(databases);
        return services.AddWorldContexts();
    }

    /// <summary>
    /// The per-world context factories and the per-request world. The repositories themselves come
    /// from AddWorldRepositories/AddCharacterRepositories (the API's shared registration).
    /// </summary>
    private static IServiceCollection AddWorldContexts(this IServiceCollection services)
    {
        services.AddSingleton<IWorldDbContextFactory, ConfiguredWorldDbContextFactory>();
        services.AddSingleton(sp => new ApiDatabaseMigrator(sp.GetRequiredService<ILogger<ApiDatabaseMigrator>>()));
        services.AddSingleton<IWorldRepositories, WorldRepositories>();
        services.AddScoped<CurrentWorld>();
        services.AddScoped<ICurrentWorld>(sp => sp.GetRequiredService<CurrentWorld>());
        services.AddSingleton<IDbContextFactory<WorldDbContext>>(sp => new CurrentWorldDbContextFactory<WorldDbContext>(
            sp.GetRequiredService<IHttpContextAccessor>(), sp.GetRequiredService<IWorldDbContextFactory>(),
            static (contexts, world) => contexts.CreateWorld(world)));
        services.AddSingleton<IDbContextFactory<CharacterDbContext>>(sp => new CurrentWorldDbContextFactory<CharacterDbContext>(
            sp.GetRequiredService<IHttpContextAccessor>(), sp.GetRequiredService<IWorldDbContextFactory>(),
            static (contexts, world) => contexts.CreateCharacters(world)));
        return services;
    }
}
