using Avalon.Common.Mathematics;
using Avalon.Common.Utils;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Auth;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Maps;
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

    IInstanceRegistry InstanceRegistry { get; }

    /// <summary>All map templates loaded by the map manager. Convenience accessor for handlers.</summary>
    IReadOnlyList<MapTemplate> MapTemplates { get; }

    StaticData Data { get; }

    void SpawnInInstance(IWorldConnection connection, IMapInstance instance);
    void TransferPlayer(IWorldConnection connection, IMapInstance targetInstance);
    Task DeSpawnPlayerAsync(IWorldConnection connection);
}

public class World : IWorld
{
    private const ushort WorldTimersCount = 5;
    private const ushort HotReloadTimer = 0;
    private readonly IOptions<GameConfiguration> _configuration;

    private readonly IChunkLibrary _chunkLibrary;
    private readonly ILogger<World> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IAvalonMapManager _mapManager;
    private readonly IScriptHotReloader _scriptHotReloader;
    private readonly IServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IntervalTimer[] _timers = new IntervalTimer[WorldTimersCount];
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
        ISpellTemplateRepository spellTemplateRepository,
        ICharacterLevelExperienceRepository characterLevelExperienceRepository,
        IScriptHotReloader scriptHotReloader,
        IChunkLibrary chunkLibrary)
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
            spellTemplateRepository, characterLevelExperienceRepository);

        for (int i = 0; i < WorldTimersCount; ++i)
        {
            _timers[i] = new IntervalTimer();
            _timers[i].SetInterval(5000);
        }
    }

    public WorldId Id => Configuration.WorldId;
    public string MinVersion => _world?.MinVersion ?? throw new InvalidOperationException("World not loaded.");
    public string CurrentVersion => _world?.Version ?? throw new InvalidOperationException("World not loaded.");
    public GameConfiguration Configuration => _configuration.Value;
    public IInstanceRegistry InstanceRegistry { get; private set; } = null!;
    public IReadOnlyList<MapTemplate> MapTemplates => _mapManager.Templates;
    public StaticData Data { get; }

    public void SpawnInInstance(IWorldConnection connection, IMapInstance instance) =>
        instance.AddCharacter(connection);

    public void TransferPlayer(IWorldConnection connection, IMapInstance targetInstance)
    {
        IMapInstance? current = InstanceRegistry.GetInstanceById(connection.Character!.InstanceId);
        current?.RemoveCharacter(connection);

        // Position is set by the caller (EnterMapHandler / CharacterSelectHandler) which knows
        // the canonical Layout.EntrySpawnWorldPos. TransferPlayer owns instance membership only.
        connection.Character.InstanceId = targetInstance.InstanceId;
        targetInstance.AddCharacter(connection);
    }

    public async Task DeSpawnPlayerAsync(IWorldConnection connection)
    {
        if (connection.Character is null)
            return;

        try
        {
            IMapInstance? instance =
                InstanceRegistry.GetInstanceById(connection.Character.InstanceId);
            instance?.RemoveCharacter(connection);

            await using AsyncServiceScope scope = _serviceScopeFactory.CreateAsyncScope();
            ICharacterRepository characterRepository =
                scope.ServiceProvider.GetRequiredService<ICharacterRepository>();

            CharacterEntity? entity = connection.Character! as CharacterEntity;
            Character dbCharacter = entity!.Data!;

            if (connection.Character.IsDead)
            {
                await ApplyDeathLogoutAsync(connection.Character, dbCharacter,
                    scope.ServiceProvider.GetRequiredService<IRespawnTargetResolver>(),
                    CancellationToken.None);

                var townTemplate = _mapManager.Templates.FirstOrDefault(t => t.Id == new MapTemplateId(dbCharacter.Map));
                dbCharacter.X = townTemplate?.DefaultSpawnX ?? 0f;
                dbCharacter.Y = townTemplate?.DefaultSpawnY ?? 0f;
                dbCharacter.Z = townTemplate?.DefaultSpawnZ ?? 0f;
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
            await characterRepository.UpdateAsync(dbCharacter);
        }
        catch (InvalidOperationException)
        {
        } // Ignore if character is not found
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to save character {CharacterId} on world de-spawn",
                connection.Character!.Guid);
        }
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
        GameTime.UpdateGameTimers(deltaTime);

        // Apply any pending hot-reload on the tick thread to avoid racing with instance.Update()
        List<Type>? pendingReload = Interlocked.Exchange(ref _pendingHotReload, null);
        if (pendingReload != null)
        {
            ApplyScriptsHotReload(pendingReload);
            _logger.LogInformation("Hot reloaded {Count} AI scripts", pendingReload.Count);
        }

        for (int i = 0; i < WorldTimersCount; ++i)
        {
            if (_timers[i].GetCurrent() >= 0)
            {
                _timers[i].Update((long)deltaTime.TotalMilliseconds);
            }
            else
            {
                _timers[i].SetCurrent(0);
            }
        }

        if (_timers[HotReloadTimer].Passed())
        {
            _scriptHotReloader.Update(out List<Type> scriptTypes);
            if (scriptTypes.Count > 0)
            {
                _pendingHotReload = scriptTypes;
            }

            _timers[HotReloadTimer].Reset();
        }

        foreach (IMapInstance instance in InstanceRegistry.ActiveInstances)
        {
            instance.Update(deltaTime);
        }

        InstanceRegistry.ProcessExpiredInstances(TimeSpan.FromMinutes(15));
    }

    /// <summary>
    /// Applies the "logout while dead" branch in isolation — resolves the respawn town,
    /// revives the live entity, and rewrites the persisted Character row to land at the
    /// town with full HP. No outbound packets are sent (the connection is gone). Public
    /// so the unit-test for this branch can drive it without standing up the full
    /// DeSpawnPlayerAsync DI graph.
    /// </summary>
    public static async Task ApplyDeathLogoutAsync(
        ICharacter character,
        Character dbCharacter,
        IRespawnTargetResolver resolver,
        CancellationToken ct)
    {
        var townId = await resolver.ResolveTownAsync(new MapTemplateId(character.Map.Value), ct);
        character.Revive();
        dbCharacter.Map = townId.Value;
        dbCharacter.Health = (int)character.Health;
        // Position is overwritten by the caller (DeSpawnPlayerAsync) using
        // MapTemplate.DefaultSpawn{X,Y,Z}.
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
