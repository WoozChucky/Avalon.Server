using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;

namespace Avalon.World.Public.Scripts;

public abstract class AbilityScript(IAbility ability, IUnit caster, AbilityAim aim) : IWorldObject
{
    private List<AbilityScript>? _chainedScripts;

    protected IUnit Caster { get; } = caster;

    /// <summary>What the cast aimed at when it started (#164). There is no target: the shape decides who is affected.</summary>
    protected AbilityAim Aim { get; } = aim;

    /// <summary>
    /// Where the cast resolves from (#648): the caster's position when the cast started, captured on the aim,
    /// or its current one for an aim that carries none.
    /// </summary>
    protected Vector3 Origin => Aim.Origin ?? Caster.Position;

    protected IAbility Ability { get; } = ability;

    /// <summary>
    /// The scripts chained to this one, in the order they were chained. The list is made the first time it is asked
    /// for (#925): a script that chains nothing and never looks at it, as most do, builds none.
    /// </summary>
    protected List<AbilityScript> ChainedScripts
    {
        get => _chainedScripts ??= [];
        private set => _chainedScripts = value;
    }

    protected GameEntityFields _dirtyFields;

    public GameEntityFields ConsumeDirtyFields()
    {
        GameEntityFields dirty = _dirtyFields;
        _dirtyFields = GameEntityFields.None;
        return dirty;
    }

    /// <summary>Whether a change is still waiting to be taken by <see cref="ConsumeDirtyFields" />. Read-only.</summary>
    public bool HasUnsentChanges => _dirtyFields != GameEntityFields.None;

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
        if (_chainedScripts is null)
        {
            return;
        }

        foreach (AbilityScript script in _chainedScripts)
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

        // A list of the clone's own, never the one the copy above shares with this script. With no list yet, the
        // clone makes its own when it first asks.
        if (_chainedScripts is { } chained)
        {
            var copies = new List<AbilityScript>(chained.Count);
            foreach (AbilityScript script in chained)
                copies.Add(script.Clone());
            clone.ChainedScripts = copies;
        }

        return clone;
    }
}
