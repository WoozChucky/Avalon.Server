using Avalon.Auth.Database.Repositories;
using Avalon.Domain.Auth;
using Avalon.Game.Configuration;
using Microsoft.Extensions.Options;

namespace Avalon.World;

public interface IWorld
{
    WorldId Id { get; }
    string MinVersion { get; }
    string CurrentVersion { get; }
    GameConfiguration Configuration { get; }
}

public class World(IOptions<GameConfiguration> configuration, IWorldRepository worldRepository) : IWorld
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
        
    }
}
