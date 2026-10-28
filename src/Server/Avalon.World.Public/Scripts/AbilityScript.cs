using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;

namespace Avalon.World.Public.Scripts;

public abstract class AbilityScript : IWorldObject
{
    private List<AbilityScript>? _chainedScripts;

    /// <summary>The script of one cast, which <see cref="Caster" />, <see cref="Aim" /> and <see cref="Ability" /> answer.</summary>
    protected AbilityScript(IAbility ability, IUnit caster, AbilityAim aim)
    {
        Ability = ability;
        Caster = caster;
        Aim = aim;
    }

    /// <summary>
    /// For a script that keeps its cast in fields of its own and overrides all three of <see cref="Caster" />,
    /// <see cref="Aim" /> and <see cref="Ability" />, which answer nothing here (#1181 Part D).
    /// </summary>
    protected AbilityScript()
    {
        Ability = null!;
        Caster = null!;
    }

    // Caster, Aim and Ability are virtual so that a script the World reuses for another cast (a pooled projectile, #1181
    // Part D) answers them from fields of its own, which only the World-side code that built it sets. None of them is
    // settable here or by an override's caller: each is a script's read-only view of its own cast, and the World never
    // reads a script's copy of them, keeping its own caster and ability for every cast it runs.

    /// <summary>Who cast it. Virtual only so a reused script answers it from its own field; never settable.</summary>
    protected virtual IUnit Caster { get; }

    /// <summary>
    /// What the cast aimed at when it started (#164). There is no target: the shape decides who is affected. Virtual only
    /// so a reused script answers it from its own field; never settable.
    /// </summary>
    protected virtual AbilityAim Aim { get; }

    /// <summary>
    /// Where the cast resolves from (#648): the caster's position when the cast started, captured on the aim,
    /// or its current one for an aim that carries none.
    /// </summary>
    protected Vector3 Origin => Aim.Origin ?? Caster.Position;

    /// <summary>
    /// The cast's own copy of the ability. Virtual only so a reused script answers it from its own field; never settable.
    /// </summary>
    protected virtual IAbility Ability { get; }

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

    /// <summary>
    /// This class's part of reusing a script for another cast (#1181 Part D): no change left unsent and no script chained,
    /// as a new script starts. Protected, so a script puts back only its own state, which it could already change; the
    /// rest of a reuse is the reusing script's own.
    /// </summary>
    protected void ResetForReuse()
    {
        _dirtyFields = GameEntityFields.None;
        _chainedScripts?.Clear();
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
