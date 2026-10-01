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
using Avalon.World.Loot;
using Avalon.World.Maps;
using Avalon.World.Parties;
using Avalon.World.Persistence;
using Avalon.World.Public.Combat;
using Avalon.World.Pvp;
using Avalon.World.Quests;
using Avalon.World.Reload;
using Avalon.World.Respawn;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        services
            .AddAuthDatabase() //TODO: World should not depend on Auth database
            .AddCharacterDatabase()
            .AddWorldDatabase()
            .ValidateDatabasesOnStart(DatabaseConnections.Auth | DatabaseConnections.Characters | DatabaseConnections.World)
            .AddCache();

        services.AddSingleton<IWorld, Avalon.World.World>();
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

        // Combat (Phase D): V1 uses default CombatConfig values. EncounterRegistry +
        // CombatService are constructed per MapInstance, not registered as singletons.
        services.AddSingleton<CombatConfig>();

        // Chat commands
        services.AddSingleton<ICommand, ReloadCommand>();
        services.AddSingleton<ICommand, GodModeCommand>();
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
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();

        services.AddSingleton<IReferenceDataReloader, ReferenceDataReloader>();
        services.AddSingleton<ReloadRequestHandler>();
        services.AddSingleton<ScriptCatalogPublisher>();

        return services;
    }
}
