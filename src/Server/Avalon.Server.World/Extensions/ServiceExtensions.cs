using Avalon.Database.Auth.Extensions;
using Avalon.Database.Character.Extensions;
using Avalon.Database.World.Extensions;
using Avalon.Infrastructure.Extensions;
using Avalon.World;
using Avalon.World.Chat;
using Avalon.World.Creatures;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Maps;
using Avalon.World.ChunkLayouts;
using Avalon.World.Public.Combat;
using Avalon.World.Respawn;
using Microsoft.Extensions.Logging;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Avalon.Server.World.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddWorldServices(this IServiceCollection services)
    {
        services
            .AddOptions<GameConfiguration>()
            .BindConfiguration("Game")
            .PostConfigure<IConfiguration>((gameConfig, config) =>
            {
                gameConfig.WorldId =
                    config.GetSection("Game:WorldId").Value ??
                    throw new InvalidOperationException("WorldId is not set in configuration.");
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<RegenConfiguration>()
            .BindConfiguration("Regen")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddAuthDatabase() //TODO: World should not depend on Auth database
            .AddCharacterDatabase()
            .AddWorldDatabase()
            .AddCache();

        services.AddSingleton<IWorld, Avalon.World.World>();
        services.AddSingleton<IAvalonMapManager, AvalonMapManager>();
        services.AddSingleton<IScriptManager, ScriptManager>();
        // Lazy deliberately. WorldServer.ExecuteAsync awaits _creatureSpawner.LoadAsync() BEFORE
        // _world.LoadAsync(), and the latter is what populates StaticData — so a deriver built eagerly
        // here would be built from empty collections, and CreatureStatDeriver throws on empty base
        // stats, taking down world startup. Deferring to first use is safe because spawning only
        // happens when a player enters a map, long after both loads have finished.
        services.AddSingleton(provider => new Lazy<CreatureStatDeriver>(() =>
        {
            IWorld world = provider.GetRequiredService<IWorld>();

            return new CreatureStatDeriver(
                world.Data.CreatureBaseStats,
                world.Data.CreatureRarityModifiers,
                provider.GetRequiredService<ILoggerFactory>());
        }));

        services.AddSingleton<ICreatureSpawner, CreatureSpawner>();
        services.AddSingleton<IChunkLibrary, ChunkLibrary>();
        services.AddSingleton<PredefinedChunkLayoutSource>();
        services.AddSingleton<ProceduralChunkLayoutSource>();
        services.AddSingleton<IChunkLayoutSourceResolver, ChunkLayoutSourceResolver>();
        services.AddSingleton<IChunkLayoutNavmeshBuilder, ChunkLayoutNavmeshBuilder>();
        services.AddSingleton<ICreaturePlacementService, CreaturePlacementService>();
        services.AddSingleton<IPortalPlacementService, PortalPlacementService>();
        services.AddSingleton<IChunkLayoutInstanceFactory, ChunkLayoutInstanceFactory>();
        // Scripting
        services.AddSingleton<IScriptCompiler, ScriptCompiler>();
        services.AddSingleton<IScriptHotReloader, ScriptHotReloader>();
        services.AddSingleton<IScriptDatabase, ScriptDatabase>();

        //services.AddSingleton<IQuestManager, QuestManager>();

        services.AddSingleton<IRespawnTargetResolver, RespawnTargetResolver>();

        // Combat (Phase D): V1 uses default CombatConfig values. EncounterRegistry +
        // CombatService are constructed per MapInstance, not registered as singletons.
        services.AddSingleton<CombatConfig>();

        // Chat commands
        services.AddSingleton<ICommand, GroupInviteCommand>();
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();

        return services;
    }
}
