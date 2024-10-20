using Avalon.Configuration;
using Avalon.Database.Character.Repositories;
using Avalon.Database.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.Database.Character.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddCharacterDatabase(this IServiceCollection services, string databaseSection = "Database")
    {
        services.AddAvalonDatabases(databaseSection);
        services.AddScoped<CharacterDbContext>(provider =>
        {
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var options = provider.GetRequiredService<IOptionsSnapshot<DatabaseConfiguration>>();
            return new CharacterDbContext(loggerFactory, options);
        });

        services
            .AddScoped<ICharacterRepository, CharacterRepository>()
            .AddScoped<ICharacterStatsRepository, CharacterStatsRepository>()
            .AddScoped<ICharacterSpellRepository, CharacterSpellRepository>()
            .AddScoped<ICharacterInventoryRepository, CharacterInventoryRepository>();

        return services;
    }
}
