using Avalon.Auth.Database.Repositories;
using Avalon.Common.Mathematics;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Auth;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Maps;
using Avalon.World.Pools;
using Avalon.World.Public;
using Avalon.World.Public.Creatures;
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

    Task Update(TimeSpan deltaTime);
    Task SpawnPlayerAsync(IWorldConnection connection);
    Task DespawnPlayerAsync(IWorldConnection connection);
    void OnScriptsHotReload(List<Type> aiScriptTypes);
}

public class World : IWorld
{
    public WorldId Id => Configuration.WorldId;
    public string MinVersion => _world?.MinVersion ?? throw new InvalidOperationException("World not loaded.");
    public string CurrentVersion => _world?.Version ?? throw new InvalidOperationException("World not loaded.");
    public GameConfiguration Configuration => _configuration.Value;
    public WorldGrid Grid { get; private set; }

    private Avalon.Domain.Auth.World? _world;
    private readonly ILogger<World> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IOptions<GameConfiguration> _configuration;
    private readonly IWorldRepository _worldRepository;
    private readonly IAvalonMapManager _mapManager;
    private readonly IPoolManager _poolManager;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public World(ILoggerFactory loggerFactory,
        IOptions<GameConfiguration> configuration, 
        IWorldRepository worldRepository, 
        IAvalonMapManager mapManager,
        IPoolManager poolManager,
        IServiceScopeFactory serviceScopeFactory)
    {
        _logger = loggerFactory.CreateLogger<World>();
        _loggerFactory = loggerFactory;
        _configuration = configuration;
        _worldRepository = worldRepository;
        _mapManager = mapManager;
        _poolManager = poolManager;
        _serviceScopeFactory = serviceScopeFactory;
    }

    public async Task LoadAsync(CancellationToken token)
    {
        var world = await _worldRepository.FindByIdAsync(Id);

        _world = world ?? throw new InvalidOperationException($"World {Id} not found.");

        await _mapManager.LoadAsync();

        Grid = new WorldGrid();

        var chunkId = 0U;
        
        await foreach (var (virtualMap, mapTemplate) in _mapManager.EnumerateOpenWorldAsync(token))
        {
            var chunksMetadata = virtualMap.Chunks;

            var chunks = new Dictionary<Vector2, Chunk>();
            
            foreach (var chunkMetadata in chunksMetadata)
            {
                var chunk = new Chunk(_loggerFactory)
                {
                    Id = chunkId++,
                    Enabled = false,
                    Metadata = chunkMetadata,
                    Neighbors = [] // Fills after loading all chunks
                };
                
                var key = new Vector2(chunkMetadata.Position.x, chunkMetadata.Position.z);
                chunks[key] = chunk;
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
        
        Grid.SpawnStartingEntities(_poolManager);
    }
    
    public async Task Update(TimeSpan deltaTime)
    {
        var tasks = Grid.Maps
            .AsParallel()                                     // Process maps in parallel
            .SelectMany(map => map.Chunks.Values.AsParallel() // Process chunks in parallel within each map
                .Where(chunk => chunk.Enabled)                // Only process enabled chunks
                .Select(chunk => chunk.Update(deltaTime)))
            .ToList();

        await Task.WhenAll(tasks);
    }

    public Task SpawnPlayerAsync(IWorldConnection connection)
    {
        Grid.AddPlayer(connection);
        return Task.CompletedTask;
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
        var chunks = Grid.Maps.AsParallel().SelectMany(map => map.Chunks.Values);
        var scriptTypeDict = aiScriptTypes.ToDictionary(t => t.Name, StringComparer.InvariantCultureIgnoreCase);
        var serviceProvider = _serviceScopeFactory.CreateScope().ServiceProvider;
        
        Parallel.ForEach(chunks, chunk =>
        {
            var creatures = chunk.GetCreatures();
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
