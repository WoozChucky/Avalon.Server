using Avalon.Auth.Database.Repositories;
using Avalon.Common.Mathematics;
using Avalon.Common.Utils;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Auth;
using Avalon.World.Configuration;
using Avalon.World.Database.Repositories;
using Avalon.World.Entities;
using Avalon.World.Maps;
using Avalon.World.Pools;
using Avalon.World.Public;
using Avalon.World.Public.Creatures;
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

    void Update(TimeSpan deltaTime);
    void SpawnPlayer(IWorldConnection connection);
    Task DespawnPlayerAsync(IWorldConnection connection);
    void OnScriptsHotReload(List<Type> aiScriptTypes);
    StaticData Data { get; }
}

public class World : IWorld
{
    public WorldId Id => Configuration.WorldId;
    public string MinVersion => _world?.MinVersion ?? throw new InvalidOperationException("World not loaded.");
    public string CurrentVersion => _world?.Version ?? throw new InvalidOperationException("World not loaded.");
    public GameConfiguration Configuration => _configuration.Value;
    public WorldGrid Grid { get; private set; }
    public StaticData Data { get; private set; }

    private const ushort WorldTimersCount = 5;
    private const ushort HotReloadTimer = 0;
    
    private Avalon.Domain.Auth.World? _world;
    
    private readonly ILogger<World> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IOptions<GameConfiguration> _configuration;
    private readonly IWorldRepository _worldRepository;
    private readonly IAvalonMapManager _mapManager;
    private readonly IPoolManager _poolManager;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IScriptHotReloader _scriptHotReloader;
    private readonly IntervalTimer[] _timers = new IntervalTimer[WorldTimersCount];

    public World(ILoggerFactory loggerFactory,
        IOptions<GameConfiguration> configuration,
        IWorldRepository worldRepository, 
        IAvalonMapManager mapManager,
        IPoolManager poolManager,
        IServiceScopeFactory serviceScopeFactory,
        ICharacterCreateInfoRepository characterCreateInfoRepository,
        IClassLevelStatRepository classLevelStatRepository,
        IItemTemplateRepository itemTemplateRepository,
        IScriptHotReloader scriptHotReloader)
    {
        _logger = loggerFactory.CreateLogger<World>();
        _loggerFactory = loggerFactory;
        _configuration = configuration;
        _worldRepository = worldRepository;
        _mapManager = mapManager;
        _poolManager = poolManager;
        _serviceScopeFactory = serviceScopeFactory;
        _scriptHotReloader = scriptHotReloader;
        Data = new StaticData(characterCreateInfoRepository, classLevelStatRepository, itemTemplateRepository);
        
        for (var i = 0; i < WorldTimersCount; ++i)
        {
            _timers[i] = new IntervalTimer();
            _timers[i].SetInterval(5000);
        }
    }

    public async Task LoadAsync(CancellationToken token)
    {
        var world = await _worldRepository.FindByIdAsync(Id);

        _world = world ?? throw new InvalidOperationException($"World {Id} not found.");
        
        await Data.LoadAsync();

        await _mapManager.LoadAsync();

        Grid = new WorldGrid();

        var chunkId = 1U;
        
        await foreach (var (virtualMap, mapTemplate) in _mapManager.EnumerateOpenWorldAsync(token))
        {
            var chunksMetadata = virtualMap.Chunks;

            //var chunks = new Dictionary<Vector2, Chunk>();
            var chunks = new List<Chunk>();
            
            foreach (var chunkMetadata in chunksMetadata)
            {
                var chunk = new Chunk(
                    _loggerFactory, 
                    virtualMap.Id,
                    new Vector2(chunkMetadata.Position.x, chunkMetadata.Position.z),
                    _poolManager)
                {
                    Id = chunkId++,
                    Enabled = false,
                    Metadata = chunkMetadata,
                    Neighbors = [] // Fills after loading all chunks
                };

                await chunk.InitializeAsync();
                
                chunks.Add(chunk);
            }
            
            var map = new Map(_loggerFactory)
            {
                Id = mapTemplate.Id,
                Metadata = mapTemplate,
                Size = virtualMap.Size,
                Chunks = chunks
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
        
        for (var i = 0; i < WorldTimersCount; ++i)
        {
            if (_timers[i].GetCurrent() >= 0)
                _timers[i].Update((long) deltaTime.TotalMilliseconds);
            else
                _timers[i].SetCurrent(0);
        }

        if (_timers[HotReloadTimer].Passed()) // Hot reload scripts timer
        {
            _scriptHotReloader.Update(out var scriptTypes);
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
        
        foreach (var gridMap in Grid.Maps)
        {
            foreach (var chunk in gridMap.Chunks)
            {
                if (chunk.Enabled)
                {
                    chunk.Update(deltaTime);
                }
            }
        }
    }

    public void SpawnPlayer(IWorldConnection connection)
    {
        Grid.AddPlayer(connection);
    }

    public async Task DespawnPlayerAsync(IWorldConnection connection)
    {
        _logger.LogDebug("Was called!");

        try
        {
            Grid.RemovePlayer(connection);

            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var characterRepository = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();

            var entity = connection.Character! as CharacterEntity;
            var dbCharacter = entity!.Data!;
            // This probably should be converted to a logout function
            dbCharacter.X = entity.Position.x;
            dbCharacter.Y = entity.Position.y;
            dbCharacter.Z = entity.Position.z;
            dbCharacter.Online = false;
            dbCharacter.LevelTime += (ulong) (DateTime.UtcNow - entity.EnteredWorld).TotalSeconds;
            dbCharacter.TotalTime += (ulong) (DateTime.UtcNow - entity.EnteredWorld).TotalSeconds;
            await characterRepository.UpdateAsync(dbCharacter);
        }
        catch (InvalidOperationException) { } // Ignore if character is not found
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to save character {CharacterId} on world despawn", connection.Character!.Id);
        }
    }

    public void OnScriptsHotReload(List<Type> aiScriptTypes)
    {
        var chunks = Grid.Maps.AsParallel().SelectMany(map => map.Chunks);
        var scriptTypeDict = aiScriptTypes.ToDictionary(t => t.Name, StringComparer.InvariantCultureIgnoreCase);
        var serviceProvider = _serviceScopeFactory.CreateScope().ServiceProvider;
        
        Parallel.ForEach(chunks, chunk =>
        {
            var creatures = chunk.Creatures.Values;
            var entitiesNeedingUpdate = new List<(ICreature, Type)>();
            foreach (var entity in creatures) // GetCreatures() return a copy of the list
            {
                if (!string.IsNullOrWhiteSpace(entity.ScriptName) && scriptTypeDict.TryGetValue(entity.ScriptName, out var scriptType))
                {
                    entitiesNeedingUpdate.Add((entity, scriptType));
                }
            }
            
            foreach (var (entity, scriptType) in entitiesNeedingUpdate)
            {
                chunk.RemoveCreature(entity.Id);
                var script = ActivatorUtilities.CreateInstance(serviceProvider, scriptType, [entity, chunk]) as AiScript;
                entity.Script = script;
                chunk.AddCreature(entity);
            }
        });
        
    }
    
}
