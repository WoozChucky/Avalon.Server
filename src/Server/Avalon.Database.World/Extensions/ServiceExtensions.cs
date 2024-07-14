using Avalon.Configuration;
using Avalon.Database.Extensions;
using Avalon.World.Database.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.World.Database.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddWorldDatabase(this IServiceCollection services, string databaseSection = "Database")
    {
        services.AddAvalonDatabases(databaseSection);
        services.AddScoped<WorldDbContext>(provider =>
        {
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var options = provider.GetRequiredService<IOptionsSnapshot<DatabaseConfiguration>>();
            return new WorldDbContext(loggerFactory, options);
        });

        services
            .AddScoped<ICreatureTemplateRepository, CreatureTemplateRepository>()
            .AddScoped<IMapTemplateRepository, MapTemplateRepository>()
            .AddScoped<IItemTemplateRepository, ItemTemplateRepository>()
            .AddScoped<IClassLevelStatRepository, ClassLevelStatRepository>()
            .AddScoped<ICharacterCreateInfoRepository, CharacterCreateInfoRepository>()
            .AddScoped<ICharacterLevelExperienceRepository, CharacterLevelExperienceRepository>();

        return services;
    }
}
