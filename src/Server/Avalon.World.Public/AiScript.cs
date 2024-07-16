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
    
    public virtual void Update(TimeSpan deltaTime)
    {
        foreach (var script in ChainedScripts)
        {
            if (script.ShouldRun())
                script.Update(deltaTime);
        }
    }
    
    public abstract object State { get; set; }
    
    protected abstract bool ShouldRun();
    
    public virtual void OnCharacterInteraction(ICharacter character)
    {
        
    }
    
    public virtual void OnHit(ICreature attacker, uint damage)
    {
        
    }
    
    public virtual void OnHit(ICharacter attacker, uint damage)
    {
        
    }
}
