using System.Collections.Concurrent;
using System.Drawing;
using Avalon.Domain.World;
using Avalon.Game.Creatures;
using Avalon.Game.Maps.Virtual;
using Avalon.Network.Packets.Movement;

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
    
    public ConcurrentDictionary<int, AvalonSession> Sessions { get; }
    
    public IEnumerable<MapEvent> Events => VirtualizedMap.Events;

    // Map configuration from database
    private readonly Map _template;
    
    public MapInstance(Map template, VirtualizedMap virtualizedMap)
    {
        InstanceId = Guid.NewGuid();
        Creatures = new ConcurrentDictionary<Guid, Creature>();
        Sessions = new ConcurrentDictionary<int, AvalonSession>();
        _template = template;
        VirtualizedMap = virtualizedMap;
    }
    
    public bool AddSession(AvalonSession session, bool initialLoad = false)
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
    
    public bool RemoveSession(AvalonSession session)
    {
        return Sessions.TryRemove(session.AccountId, out _);
    }

    public bool ContainsSession(AvalonSession session)
    {
        return session.InMap && Sessions.ContainsKey(session.AccountId);
    }

    public async void Update(TimeSpan deltaTime)
    {
        
        // Currently we're going trough every session (player) and for each session, we send a packet for every other
        // player in the map, another packet for every creature in the map and another packet for every event in the map.
        // This is not optimal, but it's a good start.
        // Now what i'm going to implement is a way send a less packets with information about referred entities but in a 
        // radius of the player. This way we can reduce the amount of packets sent and the amount of data sent.
        // Also the second step is to implement a way to send packets with grouped by entities
        // (meaning a packet with a group of players, another packet with creatures and another with events).
        
        const int radius = 10;

        // Broadcast player positions
        
        foreach (var session in Sessions)
        {
            if (!session.Value.InMap || session.Value.Status != ConnectionStatus.Connected) continue;
            
            
            var playerRectangle = new Rectangle(
                (int)session.Value.Character!.Movement.Position.X,
                (int)session.Value.Character!.Movement.Position.Y,
                32,
                32);
            
            int centerX = playerRectangle.X + playerRectangle.Width / 2;
            int centerY = playerRectangle.Y + playerRectangle.Height / 2;
            
            int circleDiameter = radius * 2;
            int circleX = centerX - radius;
            int circleY = centerY - radius;
            
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
                    32,
                    32);

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
        
        // Broadcast Creature positions
        foreach (var session in Sessions)
        {
            if (!session.Value.InMap || session.Value.Status != ConnectionStatus.Connected) continue;
            
            var playerRectangle = new Rectangle(
                (int)session.Value.Character!.Movement.Position.X,
                (int)session.Value.Character!.Movement.Position.Y,
                32,
                32);
            
            int centerX = playerRectangle.X + playerRectangle.Width / 2;
            int centerY = playerRectangle.Y + playerRectangle.Height / 2;
            
            int circleDiameter = radius * 2;
            int circleX = centerX - radius;
            int circleY = centerY - radius;
            
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
                    32,
                    32);

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
