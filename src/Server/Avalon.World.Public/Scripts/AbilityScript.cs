using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;

namespace Avalon.World.Public.Scripts;

public abstract class AbilityScript(IAbility ability, IUnit caster, IUnit? target) : IWorldObject
{
    protected IUnit Caster { get; } = caster;

    protected IUnit? Target { get; } = target;

    protected IAbility Ability { get; } = ability;

    protected List<AbilityScript> ChainedScripts { get; private set; } = new();

    protected GameEntityFields _dirtyFields;

    public GameEntityFields ConsumeDirtyFields()
    {
        var dirty = _dirtyFields;
        _dirtyFields = GameEntityFields.None;
        return dirty;
    }

    public abstract object State { get; set; }


    public abstract Vector3 Position { get; set; }
    public abstract Vector3 Velocity { get; set; }
    public abstract Vector3 Orientation { get; set; }
    public abstract ObjectGuid Guid { get; set; }

    public abstract void Prepare();

    public AbilityScript Chain(AbilityScript script)
    {
        ChainedScripts.Add(script);
        return this;
    }

    public virtual void Update(TimeSpan deltaTime)
    {
        foreach (AbilityScript script in ChainedScripts)
        {
            if (script.ShouldRun())
            {
                script.Update(deltaTime);
            }
        }
    }

    protected abstract bool ShouldRun();

    public virtual AbilityScript Clone()
    {
        var clone = (AbilityScript)MemberwiseClone();
        clone.ChainedScripts = new List<AbilityScript>(ChainedScripts.Count);
        foreach (AbilityScript script in ChainedScripts)
            clone.ChainedScripts.Add(script.Clone());
        return clone;
    }
}
