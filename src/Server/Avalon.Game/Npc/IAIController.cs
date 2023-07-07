using Avalon.Game.Maps;

namespace Avalon.Game.Npc;

public interface IAIController
{
    Task LoadScripts();
    Task Update(MapInstance instance, TimeSpan deltaTime);
}

public class AIController : IAIController
{
    public async Task LoadScripts()
    {
        
    }

    public async Task Update(MapInstance instance, TimeSpan deltaTime)
    {
        
    }
}
