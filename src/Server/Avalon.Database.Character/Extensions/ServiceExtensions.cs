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
    /// <summary>The world server's registration: one Characters database, from Database:Characters.</summary>
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

        return services.AddCharacterRepositories();
    }

    /// <summary>
    /// The repositories and the transaction runner, over whatever
    /// <see cref="IDbContextFactory{CharacterDbContext}"/> the host registers: the world server's single
    /// database, or the api's per-request world (#523).
    /// </summary>
    public static IServiceCollection AddCharacterRepositories(this IServiceCollection services)
    {
        services.AddSingleton<IDbTransactionRunner<CharacterDbContext>, DbTransactionRunner<CharacterDbContext>>();

        services
            .AddSingleton<ICharacterRepository, CharacterRepository>()
            .AddSingleton<ICharacterStatsRepository, CharacterStatsRepository>()
            .AddSingleton<ICharacterAbilityRepository, CharacterAbilityRepository>()
            .AddSingleton<ICharacterInventoryRepository, CharacterInventoryRepository>()
            .AddSingleton<IItemInstanceRepository, ItemInstanceRepository>()
            .AddSingleton<ICharacterSaveRepository, CharacterSaveRepository>();

        return services;
    }
}
