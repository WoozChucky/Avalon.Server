using Avalon.Common.Mathematics;
using Avalon.Common.Utils;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Auth;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Maps;
using Avalon.World.Maps.Virtualized;
using Avalon.World.Public;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;
using Avalon.World.Public.Scripts;
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

    WorldGrid Grid { get; }
    StaticData Data { get; }

    void SpawnPlayer(IWorldConnection connection);
    Task DeSpawnPlayerAsync(IWorldConnection connection);
}

public class World : IWorld
{
    private const ushort WorldTimersCount = 5;
    private const ushort HotReloadTimer = 0;
    private readonly IOptions<GameConfiguration> _configuration;

    private readonly ILogger<World> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IAvalonMapManager _mapManager;
    private readonly IScriptHotReloader _scriptHotReloader;
    private readonly IServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IntervalTimer[] _timers = new IntervalTimer[WorldTimersCount];
    private readonly IWorldRepository _worldRepository;

    private Domain.Auth.World? _world;

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
        IScriptHotReloader scriptHotReloader)
    {
        _logger = loggerFactory.CreateLogger<World>();
        _loggerFactory = loggerFactory;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _worldRepository = worldRepository;
        _mapManager = mapManager;
        _serviceScopeFactory = serviceScopeFactory;
        _scriptHotReloader = scriptHotReloader;
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
    public WorldGrid Grid { get; private set; }
    public StaticData Data { get; }

    public void SpawnPlayer(IWorldConnection connection) => Grid.AddPlayer(connection);

    public async Task DeSpawnPlayerAsync(IWorldConnection connection)
    {
        try
        {
            Grid.RemovePlayer(connection);

            await using AsyncServiceScope scope = _serviceScopeFactory.CreateAsyncScope();
            ICharacterRepository characterRepository = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();

            CharacterEntity? entity = connection.Character! as CharacterEntity;
            Character dbCharacter = entity!.Data!;
            // This probably should be converted to a logout function
            dbCharacter.X = entity.Position.x;
            dbCharacter.Y = entity.Position.y;
            dbCharacter.Z = entity.Position.z;
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
            _logger.LogError(e, "Failed to save character {CharacterId} on world de-spawn", connection.Character!.Guid);
        }
    }

    public async Task LoadAsync(CancellationToken token)
    {
        Domain.Auth.World? world = await _worldRepository.FindByIdAsync(Id);

        _world = world ?? throw new InvalidOperationException($"World {Id} not found.");

        await Data.LoadAsync();

        await _mapManager.LoadAsync();

        Grid = new WorldGrid();

        uint chunkId = 1U;

        await foreach ((VirtualizedMap virtualMap, MapTemplate mapTemplate) in
                       _mapManager.EnumerateOpenWorldAsync(token))
        {
            List<ChunkMetadata> chunksMetadata = virtualMap.Chunks;

            List<Chunk> chunks = new();

            foreach (ChunkMetadata chunkMetadata in chunksMetadata)
            {
                Vector2 position = new(chunkMetadata.Position.x, chunkMetadata.Position.z);

                if (ActivatorUtilities.CreateInstance(_serviceProvider, typeof(Chunk), virtualMap.Id, position) is not
                    Chunk chunk)
                {
                    _logger.LogError("Failed to create chunk for map {MapId} at position {Position}", virtualMap.Id,
                        position);
                    continue;
                }

                chunk.Id = chunkId++;
                chunk.Enabled = false;
                chunk.Metadata = chunkMetadata;
                chunk.Neighbors = [];

                await chunk.InitializeAsync();

                chunks.Add(chunk);
            }

            Map map = new(_loggerFactory)
            {
                Id = mapTemplate.Id, Metadata = mapTemplate, Size = virtualMap.Size, Chunks = chunks
            };

            Grid.AddMap(map);
        }

        Grid.DetectNeighbors();

        Grid.SpawnStartingEntities();
    }

    public void Update(TimeSpan deltaTime)
    {
        GameTime.UpdateGameTimers(deltaTime);
        // TODO: Name the timers with 'constant' identifiers

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

        if (_timers[HotReloadTimer].Passed()) // Hot reload scripts timer
        {
            _scriptHotReloader.Update(out List<Type> scriptTypes);
            if (scriptTypes.Count > 0)
            {
                OnScriptsHotReload(scriptTypes);
                _logger.LogInformation("Hot reloaded {Count} AI scripts", scriptTypes.Count);
            }

            _timers[HotReloadTimer].Reset();
        }

        /*
        Parallel.ForEach(Grid.Maps, map =>
        {
            Parallel.ForEach(map.Chunks, chunk =>
            {
                if (chunk.Enabled)
                {
                    chunk.Update(deltaTime); // This might become a problem due to possible race conditions with db context operations
                }
            });
        });
        */

        foreach (Map gridMap in Grid.Maps)
        {
            foreach (Chunk chunk in gridMap.Chunks)
            {
                if (chunk.Enabled)
                {
                    chunk.Update(deltaTime);
                }
            }
        }
    }

    private void OnScriptsHotReload(List<Type> aiScriptTypes)
    {
        ParallelQuery<Chunk> chunks = Grid.Maps.AsParallel().SelectMany(map => map.Chunks);
        Dictionary<string, Type> scriptTypeDict =
            aiScriptTypes.ToDictionary(t => t.Name, StringComparer.InvariantCultureIgnoreCase);
        IServiceProvider serviceProvider = _serviceScopeFactory.CreateScope().ServiceProvider;

        Parallel.ForEach(chunks, chunk =>
        {
            IEnumerable<ICreature> creatures = chunk.Creatures.Values;
            List<(ICreature, Type)> entitiesNeedingUpdate = new();
            foreach (ICreature entity in creatures) // GetCreatures() return a copy of the list
            {
                if (!string.IsNullOrWhiteSpace(entity.ScriptName) &&
                    scriptTypeDict.TryGetValue(entity.ScriptName, out Type? scriptType))
                {
                    entitiesNeedingUpdate.Add((entity, scriptType));
                }
            }

            foreach ((ICreature entity, Type scriptType) in entitiesNeedingUpdate)
            {
                chunk.RemoveCreature(entity.Guid);
                AiScript? script =
                    ActivatorUtilities.CreateInstance(serviceProvider, scriptType, entity, chunk) as AiScript;
                entity.Script = script;
                chunk.AddCreature(entity);
            }
        });
    }
}
