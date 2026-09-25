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
            .AddSingleton<IMapChunkPlacementRepository, MapChunkPlacementRepository>();

        return services;
    }
}
