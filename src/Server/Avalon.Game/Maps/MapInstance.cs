using System.Collections.Concurrent;
using Avalon.Database.World;
using Avalon.Database.World.Model;
using Avalon.Game.Creatures;
using Avalon.Game.Maps.Virtual;

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
    
    public IEnumerable<MapEvent> Events => VirtualizedMap.Events;

    // Map configuration from database
    private readonly Map _template;

    // contains character ids that are in the map, in the future the bool will be replaced with a character object probably
    // still not sure if this is the best way to do it, since i'll be sharing references to the character object between the map and the character manager
    // (even though the character manager will be the one to create the character object and is accessed in a thread safe way)
    private readonly ConcurrentDictionary<int, bool> _characters;
    
    public MapInstance(Map template, VirtualizedMap virtualizedMap)
    {
        InstanceId = Guid.NewGuid();
        Creatures = new ConcurrentDictionary<Guid, Creature>();
        _characters = new ConcurrentDictionary<int, bool>();
        _template = template;
        VirtualizedMap = virtualizedMap;
    }
    
    public void AddCharacter(int characterId)
    {
        _characters.TryAdd(characterId, true);
    }
    
    public bool IsEmptyCharacters()
    {
        return _characters.IsEmpty;
    }
    
    public void RemoveCharacter(int characterId)
    {
        _characters.TryRemove(characterId, out _);
    }

    public bool ContainsCharacter(int characterId)
    {
        return _characters.ContainsKey(characterId);
    }
    
    public IList<int> GetCharactersIds()
    {
        // Get characters in the map (value = true)
        return _characters.Where(pair => pair.Value).Select(pair => pair.Key).ToList();
    }

    public void Update(TimeSpan deltaTime)
    {
        
    }

    public void AddCreature(Creature creature)
    {
        Creatures.TryAdd(creature.Id, creature);
    }
}
