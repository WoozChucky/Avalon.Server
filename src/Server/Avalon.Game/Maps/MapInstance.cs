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

    private readonly ServerMap _map;
    private readonly Map _template;

    private ConcurrentDictionary<int, bool> _characters; // contains character ids that are in the map
    
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
