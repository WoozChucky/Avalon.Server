using Avalon.Combat;
using Avalon.Database;
using Avalon.Database.Auth.Extensions;
using Avalon.Database.Character.Extensions;
using Avalon.Database.Extensions;
using Avalon.Database.World.Extensions;
using Avalon.Infrastructure.Extensions;
using Avalon.World;
using Avalon.World.Characters;
using Avalon.World.Chat;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Items;
using Avalon.World.Loot;
using Avalon.World.Maps;
using Avalon.World.Parties;
using Avalon.World.Persistence;
using Avalon.World.Presence;
using Avalon.World.Public.Combat;
using Avalon.World.Pvp;
using Avalon.World.Quests;
using Avalon.World.Reload;
using Avalon.World.Respawn;
using Avalon.World.Maintenance;
using Avalon.Infrastructure.WorldMaintenance;
using Avalon.World.Persistence;
using Avalon.Database.Auth.Repositories;
using Microsoft.Extensions.Options;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Abstractions;
using Avalon.World.Social;
using Avalon.World.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

        // The restart drain (#768), and the host stop timeout that covers it.
        services.AddWorldShutdown();

        services
            .AddAuthDatabase() //TODO: World should not depend on Auth database
            .AddCharacterDatabase()
            .AddWorldDatabase()
            .ValidateDatabasesOnStart(DatabaseConnections.Auth | DatabaseConnections.Characters | DatabaseConnections.World)
            .AddCache()
            .AddWorldMaintenanceControl();

        // The tick-thread assertion (#639): WorldServer binds its tick thread to it, and the world, the registry, the party
        // service, who is online and every ignore list check it before changing what only the tick may change.
        services.AddSingleton<TickThreadGuard>();
        // The admin view's presence (#639): WorldServer captures it on the tick, PresenceSnapshotService writes it to Redis.
        services.AddSingleton<PresenceCapture>();
        services.AddSingleton<IWorld, Avalon.World.World>();
        services.AddSingleton<IWorldEntryGate>(sp => new WorldEntryGate(
            sp.GetRequiredService<IOptions<GameConfiguration>>().Value.WorldId,
            sp.GetRequiredService<IWorldMaintenanceRepository>(),
            sp.GetRequiredService<IAccountRepository>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(sp => new WorldMaintenanceCoordinator(
            sp.GetRequiredService<IOptions<GameConfiguration>>().Value.WorldId,
            sp.GetRequiredService<IWorldMaintenanceRepository>(),
            sp.GetRequiredService<ICharacterSaver>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<WorldMaintenanceCoordinator>>(),
            sp.GetRequiredService<IOptions<WorldShutdownConfiguration>>(),
            sp.GetService<TickThreadGuard>()));
        services.AddSingleton<IAvalonMapManager, AvalonMapManager>();
        services.AddSingleton<IScriptManager, ScriptManager>();
        services.AddSingleton<ICreatureSpawner, CreatureSpawner>();
        services.AddSingleton<IChunkLibrary, ChunkLibrary>();
        services.AddSingleton<IItemIdAllocator, ItemIdAllocator>();
        services.AddSingleton<ICharacterEconomy, CharacterEconomy>();

        // Loot (issue #460). One clock for the allocator's free-for-all time and the pickup check.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ILootRandom>(new LootRandom(Random.Shared));
        // #506: every combat roll (dodge, crit, block, weapon, creature swing) goes through this.
        services.AddSingleton<ICombatRandom>(new CombatRandom(Random.Shared));
        services.AddSingleton<ILootRoller, LootRoller>();
        services.AddSingleton<ILootAllocator, PartyLootAllocator>();
        services.AddSingleton<ICharacterSaver, CharacterSaver>();
        services.AddSingleton<ICharacterSaveScheduler, CharacterSaveScheduler>();
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

        services.AddSingleton<QuestService>();

        // Vendor quest gates (#432), met over each character's quest log (#433).
        services.AddSingleton<IQuestProgress, QuestProgress>();

        services.AddSingleton<IRespawnTargetResolver, RespawnTargetResolver>();
        services.AddSingleton(sp => new TownReturn(
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<TownReturn>(),
            sp.GetRequiredService<IWorld>(),
            sp.GetRequiredService<IRespawnTargetResolver>(),
            sp.GetRequiredService<IChunkLibrary>()));

        // Item use: the teleport, the toolbox the item use context reaches through, and the request flow.
        services.AddSingleton<MapTeleport>();
        services.AddSingleton<ItemUseTools>();
        services.AddSingleton<ItemUseService>();

        // Combat (Phase D): V1 uses default CombatConfig values. EncounterRegistry +
        // CombatService are constructed per MapInstance, not registered as singletons.
        services.AddSingleton<CombatConfig>();

        // Chat commands
        services.AddSingleton<ICommand, ReloadCommand>();
        services.AddSingleton<ICommand, GodModeCommand>();
        services.AddSingleton<ICommand>(sp => new MaintenanceCommand(
            sp.GetRequiredService<IOptions<GameConfiguration>>().Value.WorldId,
            sp.GetRequiredService<IWorldMaintenanceRepository>(),
            sp.GetRequiredService<IWorldMaintenanceControl>(),
            sp.GetRequiredService<WorldMaintenanceCoordinator>()));
        services.AddSingleton<PvpToggle>();
        // Who is online, by id and name (#717): fed by PartyService from the world's online and offline hooks and
        // read by the party invite and the whisper, so the one instance must reach both.
        services.AddSingleton<OnlineCharacters>();
        services.AddSingleton<PartyService>();
        // One chat budget per character, shared by plain chat, /p and /w (#722); World.LeaveWorldAsync forgets it.
        services.AddSingleton<ChatRateLimiter>();
        services.AddSingleton<ICommand, PvpCommand>();
        services.AddSingleton<ICommand, InviteCommand>();
        services.AddSingleton<ICommand, LeaveCommand>();
        services.AddSingleton<ICommand, KickCommand>();
        services.AddSingleton<ICommand, PromoteCommand>();
        services.AddSingleton<ICommand, PartyExperienceCommand>();
        services.AddSingleton<ICommand, PartyChatCommand>();
        services.AddSingleton<ICommand, WhisperCommand>();
        // The ignore list (#723).
        services.AddSingleton<ICommand, IgnoreCommand>();
        services.AddSingleton<ICommand, UnignoreCommand>();
        services.AddSingleton<ICommand, IgnoreListCommand>();
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();

        services.AddSingleton<IReferenceDataReloader, ReferenceDataReloader>();
        services.AddSingleton<ReloadRequestHandler>();
        services.AddSingleton<ScriptCatalogPublisher>();

        return services;
    }

    /// <summary>World:Shutdown (#768): the restart drain's options, required and validated, and the host stop timeout.</summary>
    public static IServiceCollection AddWorldShutdown(this IServiceCollection services)
    {
        services
            .AddOptions<WorldShutdownConfiguration>()
            .BindConfiguration(WorldShutdownConfiguration.Section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The world host only: the host gives every service's stop this long in all, and its 30 s default would
        // abandon a longer drain mid-countdown, and the close and the saves after it.
        services
            .AddOptions<HostOptions>()
            .Configure<IOptions<WorldShutdownConfiguration>>((host, shutdown) =>
                host.ShutdownTimeout = shutdown.Value.DrainTime + shutdown.Value.SaveMargin);
        return services;
    }
}
