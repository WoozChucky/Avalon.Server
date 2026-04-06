using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Spells;
using Avalon.World.Public.Units;

namespace Avalon.World.Public.Scripts;

public abstract class SpellScript(ISpell spell, IUnit caster, IUnit? target) : IWorldObject
{
    protected IUnit Caster { get; } = caster;

    protected IUnit? Target { get; } = target;

    protected ISpell Spell { get; } = spell;

    protected List<SpellScript> ChainedScripts { get; private set; } = new();

    public abstract object State { get; set; }


    public abstract Vector3 Position { get; set; }
    public abstract Vector3 Velocity { get; set; }
    public abstract Vector3 Orientation { get; set; }
    public abstract ObjectGuid Guid { get; set; }

    public abstract void Prepare();

    public SpellScript Chain(SpellScript script)
    {
        ChainedScripts.Add(script);
        return this;
    }

    public virtual void Update(TimeSpan deltaTime)
    {
        foreach (SpellScript script in ChainedScripts)
        {
            if (script.ShouldRun())
            {
                script.Update(deltaTime);
            }
        }
    }

    protected abstract bool ShouldRun();

    public virtual SpellScript Clone()
    {
        var clone = (SpellScript)MemberwiseClone();
        clone.ChainedScripts = new List<SpellScript>(ChainedScripts.Count);
        foreach (SpellScript script in ChainedScripts)
            clone.ChainedScripts.Add(script.Clone());
        return clone;
    }
}
