using Avalon.Auth.Database.Repositories;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Auth;
using Avalon.Game.Configuration;
using Avalon.World.Maps;
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
    Task DespawnPlayerAsync(WorldConnection connection);
}

public class World(
    ILogger<World> logger,
    IOptions<GameConfiguration> configuration, 
    IWorldRepository worldRepository, 
    IAvalonMapManager mapManager,
    IServiceScopeFactory serviceScopeFactory
    ) : IWorld
{
    public WorldId Id => Configuration.WorldId;
    public string MinVersion => _world?.MinVersion ?? throw new InvalidOperationException("World not loaded.");
    public string CurrentVersion => _world?.Version ?? throw new InvalidOperationException("World not loaded.");
    public GameConfiguration Configuration => configuration.Value;

    private Avalon.Domain.Auth.World? _world;

    public async Task LoadAsync()
    {
        var world = await worldRepository.FindByIdAsync(Id);

        _world = world ?? throw new InvalidOperationException($"World {Id} not found.");

        await mapManager.LoadAsync();
    }
    
    public async Task Update(TimeSpan deltaTime)
    {
        await mapManager.Update(deltaTime);
    }

    public async Task DespawnPlayerAsync(WorldConnection connection)
    {
        logger.LogDebug("Was called!");
        if (mapManager.RemoveSessionFromMap(connection))
        {
            try
            {
                await using var scope = serviceScopeFactory.CreateAsyncScope();
                var characterRepository = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();
                
                // This probably should be converted to a logout function
                connection.Character!.X = connection.Character.Movement.Position.X;
                connection.Character.Y = connection.Character.Movement.Position.Y;
                connection.Character.Online = false;
                connection.Character.LevelTime += (int) (DateTime.UtcNow - connection.Character.EnteredWorld).TotalSeconds;
                connection.Character.TotalTime += (int) (DateTime.UtcNow - connection.Character.EnteredWorld).TotalSeconds;
                await characterRepository.UpdateAsync(connection.Character!);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to save character {CharacterId} on world despawn", connection.Character!.Id);
            }
        }
        else
        {
            logger.LogDebug("Connection {ConnectionId} not found in any map, was removed from the world", connection.Id);
        }
    }
}
