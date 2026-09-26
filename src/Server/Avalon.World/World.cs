using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.Utils;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Auth;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.World.Configuration;
using Avalon.World.Dialogue;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Maps;
using Avalon.World.Persistence;
using Avalon.World.ChunkLayouts;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Respawn;
using Avalon.World.Scripts.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.World;

public interface IWorld
{
    WorldId Id { get; }
    string MinVersion { get; }
    string CurrentVersion { get; }
    GameConfiguration Configuration { get; }

    /// <summary>This world's clock: start time, last tick, and the length of that tick.</summary>
    GameTime Time { get; }

    IInstanceRegistry InstanceRegistry { get; }

    /// <summary>All map templates loaded by the map manager. Convenience accessor for handlers.</summary>
    IReadOnlyList<MapTemplate> MapTemplates { get; }

    StaticData Data { get; }

    void SpawnInInstance(IWorldConnection connection, IMapInstance instance);
    void TransferPlayer(IWorldConnection connection, IMapInstance targetInstance);
    Task DeSpawnPlayerAsync(IWorldConnection connection);

    Task LoadAsync(CancellationToken token);
    void Update(TimeSpan deltaTime);
}

public class World : IWorld
{
    private readonly IOptions<GameConfiguration> _configuration;

    private readonly IChunkLibrary _chunkLibrary;
    private readonly ILogger<World> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IAvalonMapManager _mapManager;
    private readonly IScriptHotReloader _scriptHotReloader;
    private readonly IServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    /// <summary>
    ///     Paces the hot-reload poll. There used to be an array of five of these, of which four were
    ///     created and ticked every frame and never read by anything.
    /// </summary>
    private readonly IntervalTimer _hotReloadTimer = new();
    private readonly IWorldRepository _worldRepository;

    private Domain.Auth.World? _world;
    private volatile List<Type>? _pendingHotReload;

    public World(ILoggerFactory loggerFactory,
        IOptions<GameConfiguration> configuration,
        IServiceProvider serviceProvider,
        IWorldRepository worldRepository,
        IAvalonMapManager mapManager,
        IServiceScopeFactory serviceScopeFactory,
        ICharacterCreateInfoRepository characterCreateInfoRepository,
        IClassLevelStatRepository classLevelStatRepository,
        IItemTemplateRepository itemTemplateRepository,
        IAbilityTemplateRepository abilityTemplateRepository,
        ICharacterLevelExperienceRepository characterLevelExperienceRepository,
        ICreatureTemplateRepository creatureTemplateRepository,
        ICreatureBaseStatRepository creatureBaseStatRepository,
        ICreatureRarityModifierRepository creatureRarityModifierRepository,
        ILocalizedTextRepository localizedTextRepository,
        IScriptHotReloader scriptHotReloader,
        IChunkLibrary chunkLibrary,
        IDialogueRepository dialogueRepository,
        ILootTableRepository lootTableRepository)
    {
        _logger = loggerFactory.CreateLogger<World>();
        _loggerFactory = loggerFactory;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _worldRepository = worldRepository;
        _mapManager = mapManager;
        _serviceScopeFactory = serviceScopeFactory;
        _scriptHotReloader = scriptHotReloader;
        _chunkLibrary = chunkLibrary;
        Data = new StaticData(characterCreateInfoRepository, classLevelStatRepository, itemTemplateRepository,
            abilityTemplateRepository, characterLevelExperienceRepository, creatureTemplateRepository,
            creatureBaseStatRepository, creatureRarityModifierRepository, localizedTextRepository,
            dialogueRepository, lootTableRepository, loggerFactory);

        _hotReloadTimer.SetInterval(
            (long)TimeSpan.FromSeconds(configuration.Value.ScriptHotReloadIntervalSeconds).TotalMilliseconds);
    }

    public WorldId Id => Configuration.WorldId;
    public string MinVersion => _world?.MinVersion ?? throw new InvalidOperationException("World not loaded.");
    public string CurrentVersion => _world?.Version ?? throw new InvalidOperationException("World not loaded.");
    public GameConfiguration Configuration => _configuration.Value;

    public GameTime Time { get; } = new();
    public IInstanceRegistry InstanceRegistry { get; private set; } = null!;
    public IReadOnlyList<MapTemplate> MapTemplates => _mapManager.Templates;
    public StaticData Data { get; }

    public void SpawnInInstance(IWorldConnection connection, IMapInstance instance)
    {
        instance.AddCharacter(connection);

        // Marked online here rather than at select. Between the two the character is built but not
        // in the world, so a row written online there is a claim nothing can retract: the despawn
        // writes it back from an instance membership that does not exist yet.
        if (connection.Character is CharacterEntity { Data: { } row } entity)
        {
            row.Online = true;
            PersistOnline(connection, entity);
        }
    }

    /// <summary>
    /// Through the character's save chain, like every other write of the row. A separate write of the
    /// live row could land after a later save and put back an older balance, or after the despawn
    /// save and mark a character online who has already left.
    /// </summary>
    private void PersistOnline(IWorldConnection connection, CharacterEntity entity)
    {
        try
        {
            using IServiceScope scope = _serviceScopeFactory.CreateScope();
            // Fire and forget: the saver logs a failed write, and the flag goes out again with the
            // next save, which writes the whole row.
            _ = scope.ServiceProvider.GetRequiredService<ICharacterSaver>().Save(connection, entity);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to mark character {CharacterId} online", entity.Data?.Id);
        }
    }

    public void TransferPlayer(IWorldConnection connection, IMapInstance targetInstance)
    {
        // The NPC stays behind in the old instance, so an open conversation, and a bank opened in it
        // (#463), ends here; otherwise the bank would stay usable from the next map. The client hears
        // SMSG_DIALOGUE_END only when there was a conversation to end.
        if (connection.CurrentDialogue is { } open)
            NpcInteraction.EndConversation(connection, open.Npc);
        if (connection.Character is CharacterEntity entity)
            entity.OpenBankNpc = null;

        IMapInstance? current = InstanceRegistry.GetInstanceById(connection.Character!.InstanceId);
        current?.RemoveCharacter(connection);

        // Position is set by the caller (EnterMapHandler / CharacterSelectHandler) which knows
        // the canonical Layout.EntrySpawnWorldPos. TransferPlayer owns instance membership only.
        connection.Character.InstanceId = targetInstance.InstanceId;
        targetInstance.AddCharacter(connection);
    }

    public async Task DeSpawnPlayerAsync(IWorldConnection connection)
    {
        // A connection that drops while its character is waiting on the readiness barrier never
        // reached an instance, but the row was already written with Online = true by the select.
        // Adopt the pending entity so the save below runs and clears it; RemoveCharacter and
        // DropPlayerFromEncounter are both no-ops for a character that was never added.
        if (connection.Character is null && connection.TakePendingSpawn() is { } pending)
            connection.Character = pending.Character;

        if (connection.Character is not { } character)
            return;

        AsyncServiceScope? scope = null;
        Task<bool>? saved = null;

        // Everything up to the save runs on the tick, with no await before it: until the connection
        // leaves the server, the tick can still run this character's queued save acknowledgements,
        // so the snapshot must be taken here and not on the thread pool.
        try
        {
            IMapInstance? instance = LeaveInstance(connection, character);

            // Queued whatever leaving the instance did: a step there that throws must not also cost
            // the logout save, which is what clears Online and keeps everything since the last
            // periodic save.
            scope = _serviceScopeFactory.CreateAsyncScope();
            saved = QueueDespawnSave(scope.Value.ServiceProvider, character, instance);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to queue the de-spawn save of character {CharacterId}", character.Guid);
        }
        finally
        {
            // Despawned once, on every path. A character select that kicks this session despawns it
            // on the tick, ahead of the despawn its close later queues, and that second pass must
            // find nothing: it would snapshot the discarded entity again and write it behind the
            // new session's own saves. A connection left holding the entity would also keep it
            // live beside the new session's copy.
            connection.Character = null;
        }

        try
        {
            if (saved is not null)
                await saved;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to save character {CharacterId} on world de-spawn", character.Guid);
        }
        finally
        {
            if (scope is { } s)
                await s.DisposeAsync();
        }
    }

    /// <summary>
    /// Takes the character out of its instance. Each step is contained on its own, so one that
    /// throws cannot leave the character registered, and so still ticked and saved periodically,
    /// because a step before it failed.
    /// </summary>
    private IMapInstance? LeaveInstance(IWorldConnection connection, ICharacter character)
    {
        // A stale (npc, node) pair surviving a disconnect would let a reconnecting player
        // resume a conversation with an NPC that may no longer be in their (new) instance.
        connection.CurrentDialogue = null;
        // The bank opened in that conversation closes with it (#463).
        if (character is CharacterEntity entity)
            entity.OpenBankNpc = null;

        IMapInstance? instance;
        try
        {
            instance = InstanceRegistry.GetInstanceById(character.InstanceId);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to find the instance of character {CharacterId} on world de-spawn", character.Guid);
            return null;
        }

        if (instance is null)
            return null;

        // Exit-path (Phase H): drop the character from any in-progress encounter before
        // unregistering them from the instance. Single hook covers logout, alt-F4, and TCP
        // timeout — all disconnect paths flow through DeSpawnPlayerAsync. Done before
        // RemoveCharacter (and before ReviveForDeathLogout) so the encounter doesn't
        // hold a stale dead-player participant after Revive() runs.
        try
        {
            instance.CombatService.DropPlayerFromEncounter(character);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to drop character {CharacterId} from its encounter on world de-spawn", character.Guid);
        }

        // Reads connection.Character, so this has to run before the despawn releases it.
        try
        {
            instance.RemoveCharacter(connection);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to remove character {CharacterId} from instance {InstanceId} on world de-spawn",
                character.Guid, instance.InstanceId);
        }

        return instance;
    }

    /// <summary>
    /// Snapshots the character and queues its logout save, synchronously, on the tick, so a relog's
    /// WhenIdle already sees it. Null when there is nothing to save.
    /// </summary>
    private Task<bool>? QueueDespawnSave(IServiceProvider services, ICharacter character, IMapInstance? instance)
    {
        if (character is not CharacterEntity { Data: { } dbCharacter } entity)
        {
            _logger.LogWarning("Character {CharacterId} has no row to save on world de-spawn", character.Guid);
            return null;
        }

        ICharacterSaver characterSaver = services.GetRequiredService<ICharacterSaver>();
        Func<Character, CancellationToken, Task>? prepareRow = null;

        if (entity.IsDead)
        {
            try
            {
                ReviveForDeathLogout(entity, dbCharacter);
            }
            catch (Exception e)
            {
                // The row still moves to the respawn town below; the entity itself is being discarded.
                _logger.LogError(e, "Failed to revive character {CharacterId} for a logout while dead", character.Guid);
            }

            // Where it died, which is where it stays if no town can be found at all.
            dbCharacter.X = entity.Position.x;
            dbCharacter.Y = entity.Position.y;
            dbCharacter.Z = entity.Position.z;

            // Finding the respawn town needs the database, so it happens inside the chained
            // write, on the copy of the row the snapshot took.
            var diedOn = new MapTemplateId(entity.Map.Value);
            IRespawnTargetResolver resolver = services.GetRequiredService<IRespawnTargetResolver>();
            IReadOnlyList<MapTemplate> templates = _mapManager.Templates;
            ILogger logger = _logger;
            prepareRow = (row, token) => MoveToRespawnTownAsync(diedOn, row, resolver, templates, logger, token);
        }
        // If logging out from a Normal map, redirect the character to the associated town
        else if (instance?.MapType == MapType.Normal)
        {
            MapTemplate? normalTemplate =
                _mapManager.Templates.FirstOrDefault(t => t.Id == instance.TemplateId);
            if (normalTemplate?.LogoutMapId is { } logoutMapId)
            {
                MapTemplate? town = _mapManager.Templates.FirstOrDefault(t =>
                    t.Id == (MapTemplateId)logoutMapId);
                dbCharacter.Map = logoutMapId;
                dbCharacter.X = town?.DefaultSpawnX ?? 0f;
                dbCharacter.Y = town?.DefaultSpawnY ?? 0f;
                dbCharacter.Z = town?.DefaultSpawnZ ?? 0f;
            }
        }
        else
        {
            dbCharacter.X = entity.Position.x;
            dbCharacter.Y = entity.Position.y;
            dbCharacter.Z = entity.Position.z;
        }

        dbCharacter.Online = false;
        dbCharacter.LevelTime += (ulong)(DateTime.UtcNow - entity.EnteredWorld).TotalSeconds;
        dbCharacter.TotalTime += (ulong)(DateTime.UtcNow - entity.EnteredWorld).TotalSeconds;
        // Memory is authoritative (spec #459 D3): the row, the money and every dirty item and slot
        // go in one transaction, queued behind any save of this character still in flight.
        return characterSaver.SaveOnDespawnAsync(entity, prepareRow, CancellationToken.None);
    }

    public async Task LoadAsync(CancellationToken token)
    {
        Domain.Auth.World? world = await _worldRepository.FindByIdAsync(Id, false, token);
        _world = world ?? throw new InvalidOperationException($"World {Id} not found.");

        await Data.LoadAsync(token);
        await _mapManager.LoadAsync();
        await _chunkLibrary.LoadAsync(token);

        var chunkLayoutFactory = _serviceProvider.GetRequiredService<IChunkLayoutInstanceFactory>();
        InstanceRegistry = new InstanceRegistry(_loggerFactory, _mapManager, chunkLayoutFactory);
    }

    public void Update(TimeSpan deltaTime)
    {
        Time.Update(deltaTime);

        // Apply any queued content reloads before the map pass and before any instance ticks.
        // Map-pass packets (movement, attack, chat) are processed on this thread too, inside the
        // instance loop below, so none of them can see a half-reloaded area. Session-pass packets
        // (character create/select, CMSG_PONG) run earlier — in WorldServer.Update, before this
        // method is even called — so for them atomicity holds a tick later, at the top of the next
        // World.Update, not "before any packet is processed" for this one.
        Data.ApplyPending();

        // Apply any pending hot-reload on the tick thread to avoid racing with instance.Update()
        List<Type>? pendingReload = Interlocked.Exchange(ref _pendingHotReload, null);
        if (pendingReload != null)
        {
            ApplyScriptsHotReload(pendingReload);
            _logger.LogInformation("Hot reloaded {Count} AI scripts", pendingReload.Count);
        }

        // No clamp needed around Update: IntervalTimer.Update already floors its own counter at
        // zero, and nothing here can drive it negative.
        _hotReloadTimer.Update((long)deltaTime.TotalMilliseconds);

        if (_hotReloadTimer.Passed())
        {
            _scriptHotReloader.Update(out List<Type> scriptTypes);
            if (scriptTypes.Count > 0)
            {
                _pendingHotReload = scriptTypes;
            }

            // Reset keeps the remainder, so a long frame does not push the next poll late.
            _hotReloadTimer.Reset();
        }

        foreach (IMapInstance instance in InstanceRegistry.ActiveInstances)
        {
            instance.Update(deltaTime);
        }

        InstanceRegistry.ProcessExpiredInstances(TimeSpan.FromMinutes(15));
    }

    /// <summary>
    /// The tick half of "logout while dead": revives the live entity and writes its full HP to the
    /// row, before the save snapshots it. No outbound packets are sent (the connection is gone).
    /// Public so the unit test for this branch can drive it without the full despawn DI graph.
    /// </summary>
    public static void ReviveForDeathLogout(ICharacter character, Character dbCharacter)
    {
        character.Revive();
        dbCharacter.Health = (int)character.Health;
    }

    /// <summary>The town a dead logout goes to when the respawn town cannot be looked up at all.</summary>
    private static readonly MapTemplateId FallbackTownId = new(1);

    /// <summary>
    /// The database half of "logout while dead": resolves the respawn town for the map the character
    /// died on, and moves <paramref name="row" /> to it, at the town's default spawn. Runs inside the
    /// chained despawn save, on the thread pool, against the snapshot's copy of the row only.
    /// </summary>
    /// <remarks>
    /// Never throws for a failed lookup. The lookup reads the World database and the save writes the
    /// Character database; a throw here would fail the whole save and lose the character's items,
    /// money and offline flag with it. On failure the row goes to town 1's default spawn, or, when
    /// town 1 is not a known template either, stays at the map and position it died at. A cancelled
    /// save still cancels.
    /// </remarks>
    public static async Task MoveToRespawnTownAsync(
        MapTemplateId diedOn,
        Character row,
        IRespawnTargetResolver resolver,
        IReadOnlyList<MapTemplate> templates,
        ILogger logger,
        CancellationToken ct)
    {
        MapTemplateId townId;
        try
        {
            townId = await resolver.ResolveTownAsync(diedOn, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            MapTemplate? fallback = templates.FirstOrDefault(t => t.Id == FallbackTownId);
            if (fallback is null)
            {
                logger.LogError(e,
                    "Finding the respawn town for character {CharacterId}, who logged out dead on map {MapId}, failed, " +
                    "and town {FallbackTownId} is not loaded; saving it where it died",
                    row.Id.Value, diedOn.Value, FallbackTownId.Value);
                return;
            }

            logger.LogError(e,
                "Finding the respawn town for character {CharacterId}, who logged out dead on map {MapId}, failed; " +
                "saving it at town {FallbackTownId}",
                row.Id.Value, diedOn.Value, FallbackTownId.Value);
            MoveTo(row, fallback);
            return;
        }

        MapTemplate? town = templates.FirstOrDefault(t => t.Id == townId);
        row.Map = townId.Value;
        row.X = town?.DefaultSpawnX ?? 0f;
        row.Y = town?.DefaultSpawnY ?? 0f;
        row.Z = town?.DefaultSpawnZ ?? 0f;
    }

    private static void MoveTo(Character row, MapTemplate town)
    {
        row.Map = town.Id.Value;
        row.X = town.DefaultSpawnX;
        row.Y = town.DefaultSpawnY;
        row.Z = town.DefaultSpawnZ;
    }

    private void ApplyScriptsHotReload(List<Type> aiScriptTypes)
    {
        Dictionary<string, Type> scriptTypeDict =
            aiScriptTypes.ToDictionary(t => t.Name, StringComparer.InvariantCultureIgnoreCase);
        IServiceProvider serviceProvider = _serviceScopeFactory.CreateScope().ServiceProvider;

        foreach (IMapInstance instance in InstanceRegistry.ActiveInstances)
        {
            List<(ICreature creature, Type scriptType)> toUpdate = [];
            foreach (ICreature entity in instance.Creatures.Values)
            {
                if (!string.IsNullOrWhiteSpace(entity.ScriptName) &&
                    scriptTypeDict.TryGetValue(entity.ScriptName, out Type? scriptType))
                {
                    toUpdate.Add((entity, scriptType));
                }
            }

            foreach ((ICreature entity, Type scriptType) in toUpdate)
            {
                instance.RemoveCreature(entity);
                AiScript? script =
                    ActivatorUtilities.CreateInstance(serviceProvider, scriptType, entity, instance) as AiScript;
                entity.Script = script;
                instance.AddCreature(entity);
            }
        }
    }
}
