using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;

namespace Avalon.World.Public;

public abstract class AiScript {
    
    protected ICreature Creature { get; }
    protected IChunk Chunk { get; }
    
    protected List<AiScript> ChainedScripts { get; } = new();
    
    protected AiScript(ICreature creature, IChunk chunk)
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
    
    public virtual void OnCharacterInteraction(ICharacter character)
    {
        
    }
}
