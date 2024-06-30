using System.Collections.Concurrent;
using System.Drawing;
using Avalon.Domain.World;
using Avalon.Game.Configuration;
using Avalon.Game.Creatures;
using Avalon.Game.Maps.Virtual;
using Avalon.Infrastructure;
using Avalon.Network.Packets.Movement;
using Microsoft.Extensions.Logging;

namespace Avalon.Game.Maps;

public class MapInstance
{
    public Guid InstanceId { get; set; }
    public int MapId => _template.Id;
    public string Name => _template.Name;
    public string Atlas => _template.Atlas;
    public string Directory => _template.Directory;
    public string Description => _template.Description;
    
    // Map tiles virtual representation
    // Contains all the layers (tiles, creatures, objects, events) information
    public VirtualizedMap VirtualizedMap { get; }

    public ConcurrentDictionary<Guid, Creature> Creatures { get; }
    
    public ConcurrentDictionary<int, AvalonWorldSession> Sessions { get; }
    
    public IEnumerable<MapEvent> Events => VirtualizedMap.Events;

    // Map configuration from database
    private readonly Map _template;
    private readonly GameConfiguration _gameConfiguration;
    private readonly ILogger<MapInstance> _logger;
    
    public MapInstance(ILoggerFactory loggerFactory, Map template, VirtualizedMap virtualizedMap, GameConfiguration gameConfiguration)
    {
        _logger = loggerFactory.CreateLogger<MapInstance>();
        InstanceId = Guid.NewGuid();
        Creatures = new ConcurrentDictionary<Guid, Creature>();
        Sessions = new ConcurrentDictionary<int, AvalonWorldSession>();
        _template = template;
        _gameConfiguration = gameConfiguration;
        VirtualizedMap = virtualizedMap;
    }
    
    public bool AddSession(AvalonWorldSession session, bool initialLoad = false)
    {
        if (initialLoad)
        {
            return Sessions.TryAdd(session.AccountId, session);
        }
        
        // Only add the session if it's in game
        return session.InGame && Sessions.TryAdd(session.AccountId, session);
    }
    
    public bool IsEmptySessions()
    {
        return Sessions.Count == 0;
    }
    
    public bool RemoveSession(AvalonWorldSession session)
    {
        return Sessions.TryRemove(session.AccountId, out _);
    }

    public bool ContainsSession(AvalonWorldSession session)
    {
        return session.InMap && Sessions.ContainsKey(session.AccountId);
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
        foreach (var session in Sessions)
        {
            if (!session.Value.InMap || session.Value.Status != ConnectionStatus.Connected) continue;
            
            
            var playerRectangle = new Rectangle(
                (int)session.Value.Character!.Movement.Position.X,
                (int)session.Value.Character!.Movement.Position.Y,
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

            foreach (var otherSession in Sessions)
            {
                if (!otherSession.Value.InMap || otherSession.Value.Status != ConnectionStatus.Connected) continue;
                if (otherSession.Value.AccountId == session.Value.AccountId) continue;
                
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
            
            await session.Value.SendAsync(SPlayerPositionUpdatePacket.Create(playerPackets.ToArray(), session.Value.Encrypt));
        }
    }

    private async Task BroadcastCreaturesAsync(TimeSpan deltaTime)
    {
        // Broadcast Creature positions
        foreach (var session in Sessions)
        {
            if (!session.Value.InMap || session.Value.Status != ConnectionStatus.Connected) continue;
            
            var playerRectangle = new Rectangle(
                (int)session.Value.Character!.Movement.Position.X,
                (int)session.Value.Character!.Movement.Position.Y,
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
            
            await session.Value.SendAsync(SNpcUpdatePacket.Create(
                creaturePackets.ToArray(),
                session.Value.Encrypt));
        }
    }

    public void AddCreature(Creature creature)
    {
        Creatures.TryAdd(creature.Id, creature);
    }
}
