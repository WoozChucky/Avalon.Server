using Avalon.Configuration;
using Avalon.Database.Character.Repositories;
using Avalon.Database.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.Database.Character.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddCharacterDatabase(this IServiceCollection services, string databaseSection = "Database")
    {
        services.AddAvalonDatabases(databaseSection);

        // The context itself is not registered: repositories create one per call and nothing else
        // may hold one. IOptions, not IOptionsSnapshot — the factory is a singleton.
        services.AddSingleton<IDbContextFactory<CharacterDbContext>>(provider =>
        {
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var options = provider.GetRequiredService<IOptions<DatabaseConfiguration>>();
            return new DelegateDbContextFactory<CharacterDbContext>(() => new CharacterDbContext(loggerFactory, options));
        });
        services.AddSingleton<IDbTransactionRunner<CharacterDbContext>, DbTransactionRunner<CharacterDbContext>>();

        services
            .AddSingleton<ICharacterRepository, CharacterRepository>()
            .AddSingleton<ICharacterStatsRepository, CharacterStatsRepository>()
            .AddSingleton<ICharacterAbilityRepository, CharacterAbilityRepository>()
            .AddSingleton<ICharacterInventoryRepository, CharacterInventoryRepository>();

        return services;
    }
}
