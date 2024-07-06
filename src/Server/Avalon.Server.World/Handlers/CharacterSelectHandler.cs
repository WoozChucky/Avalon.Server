using System.Numerics;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Character;
using Avalon.World;
using Avalon.World.Maps;
using Microsoft.Extensions.Logging;

namespace Avalon.Server.World.Handlers;

public class CharacterSelectHandler : IWorldPacketHandler<CCharacterSelectedPacket>
{
    private readonly ILogger<CharacterSelectHandler> _logger;
    private readonly ICharacterRepository _characterRepository;
    private readonly IAvalonMapManager _mapManager;
    private readonly IWorld _world;
    
    public CharacterSelectHandler(ILogger<CharacterSelectHandler> logger, ICharacterRepository characterRepository, IAvalonMapManager mapManager, IWorld world)
    {
        _logger = logger;
        _characterRepository = characterRepository;
        _mapManager = mapManager;
        _world = world;
    }
    
    public async Task ExecuteAsync(WorldPacketContext<CCharacterSelectedPacket> ctx, CancellationToken token = default)
    {
        if (ctx.Connection.AccountId == null)
        {
            _logger.LogWarning("Connection tried to select a character list without being authenticated");
            ctx.Connection.Close();
            return;       
        }

        if (ctx.Connection.CharacterId != null)
        {
            _logger.LogWarning("Connection tried to select a character list while already having a character selected");
            ctx.Connection.Close();
            return;
        }
       
        var character = await _characterRepository.FindByIdAndAccountAsync(ctx.Packet.CharacterId, ctx.Connection.AccountId);
        if (character == null)
        {
            _logger.LogWarning("Character {CharacterId} not found for account {AccountId}", ctx.Packet.CharacterId, ctx.Connection.AccountId);
            return;
        }
        
        // Try to find the map instance for this character
        var mapInstance = _mapManager.GetInstance(character.Map, Guid.TryParse(character.InstanceId, out var instanceId) ? instanceId : Guid.Empty);
        if (mapInstance == null)
        {
            // Specific map instance not found, try to find a other instance of the same map
            // If no instance found, (internally will create a new one) add the session to it
            mapInstance = _mapManager.GetInstance(character.Map, ctx.Connection) ?? _mapManager.AddConnectionToMap(character.Map, ctx.Connection, true);
            
            if (mapInstance == null)
            {
                _logger.LogWarning("Failed to find or create map instance for character {CharacterId} for account {AccountId}", ctx.Connection.CharacterId, ctx.Connection.AccountId);
                return;
            }
        }
        else
        {
            // Map instance found, add the session to it
            if (!mapInstance.AddConnection(ctx.Connection, true))
            {
                _logger.LogWarning("Failed to add session {AccountId} to map instance {InstanceId}", ctx.Connection.AccountId, mapInstance.InstanceId);
                return;
            }
        }
        
        // Update the current map instance
        character.InstanceId = mapInstance.InstanceId.ToString();

        character.Movement = new CharacterMovement
        {
            Position = new Vector2(character.X, character.Y),
            Velocity = Vector2.Zero
        };
        
        character.Online = true;
        character.Latency = (int) ctx.Connection.Latency;
        character.EnteredWorld = DateTime.UtcNow;

        ctx.Connection.Send(SCharacterSelectedPacket.Create(new CharacterInfo
        {
            CharacterId = character.Id!.Value,
            Name = character.Name,
            Level = character.Level,
            Class = (ushort) character.Class,
            X = character.X,
            Y = character.Y,
            Radius = _world.Configuration.PlayerRadius
        },
        new MapInfo
        {
            MapId = mapInstance.MapId,
            InstanceId = mapInstance.InstanceId,
            Name = mapInstance.Name,
            Description = mapInstance.Description
        }, ctx.Connection.CryptoSession.Encrypt));

        // save the character to the database
        await _characterRepository.UpdateAsync(character);

        ctx.Connection.Character = character;
        ctx.Connection.CharacterId = character.Id;
        ctx.Connection.Character.Movement = new CharacterMovement
        {
            Position = new Vector2(character.X, character.Y),
            Velocity = Vector2.Zero
        };
        
        mapInstance.OnConnectionEntered(ctx.Connection);
        
        _logger.LogInformation("Character {CharacterId} logged in for account {AccountId} at {Position}", character.Name, ctx.Connection.AccountId, character.Movement);
    }
}
