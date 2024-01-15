using System.Collections.Concurrent;
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
        
        // Broadcast Creature positions
        foreach (var session in Sessions)
        {
            
            if (!session.Value.InMap || session.Value.Status != ConnectionStatus.Connected) continue;
            
            // Creatures
            var creatures = Creatures.Values
                .Select(c => new CreaturePacket
                {
                    Id = c.Id,
                    Name = c.Name,
                    PositionX = c.Position.X,
                    PositionY = c.Position.Y,
                    VelocityX = c.Velocity.X,
                    VelocityY = c.Velocity.Y
                })
                .ToArray();
            
            await session.Value.SendAsync(SNpcUpdatePacket.Create(
                creatures,
                session.Value.Encrypt));
        }
        
        // Broadcast player positions
        foreach (var session in Sessions)
        {
            if (!session.Value.InMap || session.Value.Status != ConnectionStatus.Connected) continue;

            var playerPackets = new List<SPlayerPacket>();
            
            foreach (var otherSession in Sessions)
            {
                if (otherSession.Value == null || !otherSession.Value.InMap || otherSession.Value.Status != ConnectionStatus.Connected) continue;
                
                playerPackets.Add(new SPlayerPacket
                {
                    AccountId = otherSession.Value.AccountId,
                    CharacterId = otherSession.Value.Character!.Id,
                    PositionX = otherSession.Value.Character.Movement.Position.X,
                    PositionY = otherSession.Value.Character.Movement.Position.Y,
                    VelocityX = otherSession.Value.Character.Movement.Velocity.X,
                    VelocityY = otherSession.Value.Character.Movement.Velocity.Y,
                    Chatting = otherSession.Value.Character!.IsChatting,
                    Elapsed = otherSession.Value.Character.ElapsedGameTime
                });
            }
            
            if (playerPackets.Count == 0) continue;

            await session.Value.SendAsync(SPlayerPositionUpdatePacket.Create(playerPackets.ToArray(), session.Value.Encrypt));
        }
    }

    public void AddCreature(Creature creature)
    {
        Creatures.TryAdd(creature.Id, creature);
    }
}
