using Avalon.Configuration;
using Avalon.Database.Extensions;
using Avalon.Database.World.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.Database.World.Extensions;

public static class ServiceExtensions
{
    /// <summary>The world server's registration: one World database, from Database:World.</summary>
    public static IServiceCollection AddWorldDatabase(this IServiceCollection services, string databaseSection = "Database")
    {
        services.AddAvalonDatabases(databaseSection);

        // The context itself is not registered: repositories create one per call and nothing else
        // may hold one. IOptions, not IOptionsSnapshot — the factory is a singleton.
        services.AddSingleton<IDbContextFactory<WorldDbContext>>(provider =>
        {
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var options = provider.GetRequiredService<IOptions<DatabaseConfiguration>>();
            return new DelegateDbContextFactory<WorldDbContext>(() => new WorldDbContext(loggerFactory, options));
        });

        return services.AddWorldRepositories();
    }

    /// <summary>
    /// The repositories and the transaction runner, over whatever
    /// <see cref="IDbContextFactory{WorldDbContext}"/> the host registers: the world server's single
    /// database, or the api's per-request world (#523).
    /// </summary>
    public static IServiceCollection AddWorldRepositories(this IServiceCollection services)
    {
        services.AddSingleton<IDbTransactionRunner<WorldDbContext>, DbTransactionRunner<WorldDbContext>>();

        services
            .AddSingleton<ICreatureTemplateRepository, CreatureTemplateRepository>()
            .AddSingleton<IMapTemplateRepository, MapTemplateRepository>()
            .AddSingleton<IItemTemplateRepository, ItemTemplateRepository>()
            .AddSingleton<IClassLevelStatRepository, ClassLevelStatRepository>()
            .AddSingleton<ICharacterCreateInfoRepository, CharacterCreateInfoRepository>()
            .AddSingleton<ICharacterLevelExperienceRepository, CharacterLevelExperienceRepository>()
            .AddSingleton<IMapCreatureSpawnRepository, MapCreatureSpawnRepository>()
            .AddSingleton<ILocalizedTextRepository, LocalizedTextRepository>()
            .AddSingleton<IDialogueRepository, DialogueRepository>()
            .AddSingleton<ICreatureBaseStatRepository, CreatureBaseStatRepository>()
            .AddSingleton<ICreatureRarityModifierRepository, CreatureRarityModifierRepository>()
            .AddSingleton<IAbilityTemplateRepository, AbilityTemplateRepository>()
            .AddSingleton<IChunkTemplateRepository, ChunkTemplateRepository>()
            .AddSingleton<IChunkPoolRepository, ChunkPoolRepository>()
            .AddSingleton<ISpawnTableRepository, SpawnTableRepository>()
            .AddSingleton<IProceduralMapConfigRepository, ProceduralMapConfigRepository>()
            .AddSingleton<IMapChunkPlacementRepository, MapChunkPlacementRepository>()
            .AddSingleton<ILootTableRepository, LootTableRepository>()
            .AddSingleton<IVendorStockRepository, VendorStockRepository>()
            .AddSingleton<ICombatDataRepository, CombatDataRepository>();

        return services;
    }
}
