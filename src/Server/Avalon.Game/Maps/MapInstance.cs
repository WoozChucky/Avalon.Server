using System.Collections.Concurrent;
using Avalon.Database.World;

namespace Avalon.Game.Maps;

public class MapInstance
{
    public Guid InstanceId { get; set; }
    public int MapId => _template.Id;
    public string Name => _template.Name;
    public string Atlas => _template.Atlas;
    public string Directory => _template.Directory;

    // Map tiles virtual representation
    // Contains all the layers (tiles, objects, events) information and their properties
    private readonly ServerMap _map;
    
    // Map configuration from database
    private readonly Map _template;

    // contains character ids that are in the map, in the future the bool will be replaced with a character object probably
    // still not sure if this is the best way to do it, since i'll be sharing references to the character object between the map and the character manager
    // (even though the character manager will be the one to create the character object and is accessed in a thread safe way)
    private ConcurrentDictionary<int, bool> _characters;
    
    public MapInstance(Map template)
    {
        InstanceId = Guid.NewGuid();
        _characters = new ConcurrentDictionary<int, bool>();
        _template = template;
        _map = new ServerMap(template.Name, template.Directory);
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

    public void Update(TimeSpan deltaTime)
    {
        
    }
}
