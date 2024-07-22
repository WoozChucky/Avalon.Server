using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Spells;

namespace Avalon.World.Public.Scripts;

public abstract class SpellScript : IWorldObject
{
    public abstract Vector3 Position { get; set; }
    public abstract Vector3 Velocity { get; set; }
    public abstract Vector3 Orientation { get; set; }
    public abstract ObjectGuid Guid { get; init; }

    protected IUnit Caster { get; }
    
    protected IUnit? Target { get; }
    
    protected ISpell Spell { get; }
    
    protected List<SpellScript> ChainedScripts { get; } = new();
    
    protected SpellScript(ISpell spell, IUnit caster, IUnit? target)
    {
        Spell = spell;
        Caster = caster;
        Target = target;
    }
    
    public SpellScript Chain(SpellScript script)
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
}
