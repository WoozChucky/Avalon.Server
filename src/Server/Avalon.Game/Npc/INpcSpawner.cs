using Avalon.Game.Maps;

namespace Avalon.Game.Npc;

public interface INpcSpawner
{
    Task LoadNpcs();
    
    Task Update(MapInstance instance, TimeSpan deltaTime);
}

public class NpcSpawner : INpcSpawner
{
    public async Task LoadNpcs()
    {
        
    }

    public async Task Update(MapInstance instance, TimeSpan deltaTime)
    {
        
    }
}
