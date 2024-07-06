using Avalon.Domain.Characters;
using Avalon.World.Entities;
using MapInstance = Avalon.World.Maps.MapInstance;

namespace Avalon.World.Scripts;

public abstract class AiScript {
    
    protected Creature Creature { get; }
    protected MapInstance Map { get; }
    
    protected List<AiScript> ChainedScripts { get; } = new();
    
    protected AiScript(Creature creature, MapInstance map)
    {
        Creature = creature;
        Map = map;
    }
    
    public AiScript Chain(AiScript script)
    {
        ChainedScripts.Add(script);
        return this;
    }
    
    public virtual async Task Update(TimeSpan deltaTime)
    {
        foreach (var script in ChainedScripts)
        {
            await script.Update(deltaTime);
        }
    }
    
    public virtual void OnCharacterInteraction(Character character)
    {
        
    }
}
