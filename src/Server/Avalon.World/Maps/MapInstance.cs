using System.Collections.Concurrent;
using System.Drawing;
using Avalon.Domain.Auth;
using Avalon.Domain.World;
using Avalon.Game.Configuration;
using Avalon.Network.Packets.Movement;
using Avalon.World.Entities;
using Avalon.World.Maps.Virtual;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Maps;

public class MapInstance
{
    public Guid InstanceId { get; set; }
    public int MapId => _template.Id;
    public string Name => _template.Name;
    public string Atlas => _template.Atlas;
    public string Directory => _template.Directory;
    public string Description => _template.Description;
    public bool IsEmpty => Connections.IsEmpty;
    
    // Map tiles virtual representation
    // Contains all the layers (tiles, creatures, objects, events) information
    public VirtualizedMap VirtualizedMap { get; }

    public ConcurrentDictionary<Guid, Creature> Creatures { get; }
    
    public ConcurrentDictionary<AccountId, IWorldConnection> Connections { get; }
    
    public IEnumerable<MapEvent> Events => VirtualizedMap.Events;

    // Map configuration from database
    private readonly MapTemplate _template;
    private readonly GameConfiguration _gameConfiguration;
    private readonly ILogger<MapInstance> _logger;
    
    public MapInstance(ILoggerFactory loggerFactory, MapTemplate template, VirtualizedMap virtualizedMap, GameConfiguration gameConfiguration)
    {
        _logger = loggerFactory.CreateLogger<MapInstance>();
        InstanceId = Guid.NewGuid();
        Creatures = new ConcurrentDictionary<Guid, Creature>();
        Connections = new ConcurrentDictionary<AccountId, IWorldConnection>();
        _template = template;
        _gameConfiguration = gameConfiguration;
        VirtualizedMap = virtualizedMap;
    }
    
    public bool AddConnection(IWorldConnection connection, bool initialLoad = false)
    {
        if (initialLoad)
        {
            return Connections.TryAdd(connection.AccountId, connection);
        }
        
        // Only add the session if it's in game
        return connection.InGame && Connections.TryAdd(connection.AccountId, connection);
    }
    
    public bool RemoveSession(IWorldConnection connection)
    {
        return Connections.TryRemove(connection.AccountId, out _);
    }

    public bool ContainsConnection(IWorldConnection connection)
    {
        return connection.InMap && Connections.ContainsKey(connection.AccountId);
    }

    private const float BroadcastInterval = 0.1f;
    private float _lastBroadcastTime;

    public async void Update(TimeSpan deltaTime)
    {
        _lastBroadcastTime += (float) deltaTime.TotalSeconds;
        if (_lastBroadcastTime >= BroadcastInterval)
        {
            _lastBroadcastTime = 0;
            await BroadcastPlayersAsync(deltaTime).ConfigureAwait(false);
            await BroadcastCreaturesAsync(deltaTime).ConfigureAwait(false);
        }
    }

    private async Task BroadcastPlayersAsync(TimeSpan deltaTime)
    {
        // Broadcast player positions
        foreach (var connection in Connections)
        {
            if (!connection.Value.InMap) continue;
            
            var playerRectangle = new Rectangle(
                (int)connection.Value.Character!.Movement.Position.X,
                (int)connection.Value.Character!.Movement.Position.Y,
                VirtualizedMap.TileWidth,
                VirtualizedMap.TileHeight);
            
            int centerX = playerRectangle.X + playerRectangle.Width / 2;
            int centerY = playerRectangle.Y + playerRectangle.Height / 2;
            
            int circleDiameter = (int) _gameConfiguration.PlayerRadius * 2;
            int circleX = (int) (centerX - _gameConfiguration.PlayerRadius);
            int circleY = (int) (centerY - _gameConfiguration.PlayerRadius);
            
            var playerRadiusRectangle = new Rectangle(
                circleX,
                circleY,
                circleDiameter,
                circleDiameter);
            
            var playerPackets = new List<SPlayerPacket>();

            foreach (var otherSession in Connections)
            {
                if (!otherSession.Value.InMap) continue;
                if (otherSession.Value.AccountId == connection.Value.AccountId) continue;
                
                var otherPlayerRectangle = new Rectangle(
                    (int)otherSession.Value.Character!.Movement.Position.X,
                    (int)otherSession.Value.Character!.Movement.Position.Y,
                    VirtualizedMap.TileWidth,
                    VirtualizedMap.TileHeight);

                if (playerRadiusRectangle.IntersectsWith(otherPlayerRectangle))
                {
                    //TODO: Add this player to the list of players to send to the current player
                    playerPackets.Add(new SPlayerPacket
                    {
                        AccountId = otherSession.Value.AccountId,
                        CharacterId = otherSession.Value.Character!.Id!.Value,
                        PositionX = otherSession.Value.Character.Movement.Position.X,
                        PositionY = otherSession.Value.Character.Movement.Position.Y,
                        VelocityX = otherSession.Value.Character.Movement.Velocity.X,
                        VelocityY = otherSession.Value.Character.Movement.Velocity.Y,
                        Chatting = otherSession.Value.Character!.IsChatting,
                        Elapsed = otherSession.Value.Character.ElapsedGameTime
                    });
                }
            }
            
            if (playerPackets.Count == 0) continue;
            
            connection.Value.Send(SPlayerPositionUpdatePacket.Create(playerPackets.ToArray(), connection.Value.CryptoSession.Encrypt));
        }
    }

    private async Task BroadcastCreaturesAsync(TimeSpan deltaTime)
    {
        // Broadcast Creature positions
        foreach (var connection in Connections)
        {
            if (!connection.Value.InMap) continue;
            
            var playerRectangle = new Rectangle(
                (int)connection.Value.Character!.Movement.Position.X,
                (int)connection.Value.Character!.Movement.Position.Y,
                VirtualizedMap.TileWidth,
                VirtualizedMap.TileHeight);
            
            int centerX = playerRectangle.X + playerRectangle.Width / 2;
            int centerY = playerRectangle.Y + playerRectangle.Height / 2;
            
            int circleDiameter = (int) _gameConfiguration.PlayerRadius * 2;
            int circleX = (int) (centerX - _gameConfiguration.PlayerRadius);
            int circleY = (int) (centerY - _gameConfiguration.PlayerRadius);
            
            var playerRadiusRectangle = new Rectangle(
                circleX,
                circleY,
                circleDiameter,
                circleDiameter);
            
            var creaturePackets = new List<CreaturePacket>();

            foreach (var creature in Creatures.Values)
            {
                
                var creatureRectangle = new Rectangle(
                    (int) creature.Position.X,
                    (int) creature.Position.Y,
                    VirtualizedMap.TileWidth,
                    VirtualizedMap.TileHeight);

                if (playerRadiusRectangle.IntersectsWith(creatureRectangle))
                {
                    creaturePackets.Add(new CreaturePacket
                    {
                        Id = creature.Id,
                        Name = creature.Name,
                        PositionX = creature.Position.X,
                        PositionY = creature.Position.Y,
                        VelocityX = creature.Velocity.X,
                        VelocityY = creature.Velocity.Y
                    });
                }
            }
            
            if (creaturePackets.Count == 0) continue;
            
            connection.Value.Send(SNpcUpdatePacket.Create(creaturePackets.ToArray(), connection.Value.CryptoSession.Encrypt));
        }
    }

    public void AddCreature(Creature creature)
    {
        Creatures.TryAdd(creature.Id, creature);
    }
}
