using Avalon.Domain.Characters;
using Avalon.World.Entities;

namespace Avalon.World.Scripts;

public abstract class AiScript {
    
    protected Creature Creature { get; }
    protected Chunk Chunk { get; }
    
    protected List<AiScript> ChainedScripts { get; } = new();
    
    protected AiScript(Creature creature, Chunk chunk)
    {
        Creature = creature;
        Chunk = chunk;
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
