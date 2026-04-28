using Avalon.Database.Auth.Extensions;
using Avalon.Database.Character.Extensions;
using Avalon.Database.World.Extensions;
using Avalon.Infrastructure.Extensions;
using Avalon.World;
using Avalon.World.Chat;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Maps;
using Avalon.World.ChunkLayouts;
using Avalon.World.Respawn;
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
            });

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

        // Chat commands
        services.AddSingleton<ICommand, GroupInviteCommand>();
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();

        return services;
    }
}
