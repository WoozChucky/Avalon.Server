using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Units;

namespace Avalon.World.Public.Scripts;

public abstract class AiScript(ICreature creature, ISimulationContext context)
{
    protected ICreature Creature { get; } = creature;
    protected ISimulationContext Context { get; } = context;

    protected List<AiScript> ChainedScripts { get; } = new();

    public abstract object State { get; set; }

    protected AiScript Chain(AiScript script)
    {
        ChainedScripts.Add(script);
        return this;
    }

    public virtual void Update(TimeSpan deltaTime)
    {
        foreach (AiScript script in ChainedScripts)
        {
            if (script.ShouldRun())
            {
                script.Update(deltaTime);
            }
        }
    }

    protected abstract bool ShouldRun();

    public virtual void OnHit(IUnit attacker, uint damage)
    {
        foreach (AiScript script in ChainedScripts)
        {
            script.OnHit(attacker, damage);
        }
    }

    public virtual void OnEnteredRange(ICharacter character)
    {
        foreach (AiScript script in ChainedScripts)
        {
            script.OnEnteredRange(character);
        }
    }
}
