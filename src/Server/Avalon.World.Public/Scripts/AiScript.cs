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

    /// <summary>
    /// <paramref name="attacker" /> attacked this creature and the hit dealt nothing: it was dodged (#506).
    /// The creature should fight back as if hit, but has taken no damage. A notification only: it grants
    /// nothing. Forwarded to chained scripts, as <see cref="OnHit" /> is.
    /// </summary>
    public virtual void OnAttacked(IUnit attacker)
    {
        foreach (AiScript script in ChainedScripts)
        {
            script.OnAttacked(attacker);
        }
    }

    public virtual void OnEnteredRange(ICharacter character)
    {
        foreach (AiScript script in ChainedScripts)
        {
            script.OnEnteredRange(character);
        }
    }

    /// <summary>
    /// <paramref name="character" /> has left this creature's instance (#546). A notification only: it
    /// grants nothing. Forwarded to chained scripts, as <see cref="OnHit" /> is.
    /// </summary>
    public virtual void OnCharacterLeft(ICharacter character)
    {
        foreach (AiScript script in ChainedScripts)
        {
            script.OnCharacterLeft(character);
        }
    }
}
