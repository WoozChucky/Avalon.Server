using Avalon.Game.Creatures;
using Avalon.Game.Maps;

namespace Avalon.Game.Scripts;

public abstract class AIScript {
    
    protected Creature Creature { get; }
    protected MapInstance Map { get; }
    
    protected List<AIScript> ChainedScripts { get; } = new();
    
    protected AIScript(Creature creature, MapInstance map)
    {
        Creature = creature;
        Map = map;
    }
    
    public AIScript Chain(AIScript script)
    {
        ChainedScripts.Add(script);
        return this;
    }
    
    public virtual void Update(TimeSpan deltaTime)
    {
        foreach (var script in ChainedScripts)
        {
            script.Update(deltaTime);
        }
    }

}
