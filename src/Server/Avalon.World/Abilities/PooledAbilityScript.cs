using Avalon.World.Public.Abilities;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;

namespace Avalon.World.Abilities;

/// <summary>
/// An ability script an instance can reuse for cast after cast (#1181 Part D), through its <see cref="AbilityScriptPool" />:
/// the base every pooled script type derives from, World-side only. It keeps its cast (its copy of the ability, its caster
/// and its aim) in fields of its own, which answer <see cref="AbilityScript" />'s getters, so the pool can bind it to the
/// next cast with <see cref="Launch" /> and let it go with <see cref="Clear" />. Nothing on the modding API becomes
/// settable, and a script outside Avalon.World cannot derive from this: its constructor and its reset hooks are not
/// visible there.
/// </summary>
/// <remarks>
/// <para>
/// <b>The reset contract.</b> <see cref="Launch" /> leaves every field as a new script of the cast would have it: the
/// copy made again from the caster's ability (<see cref="GameAbility.CopyFrom" />), the caster and the aim, the base
/// class's state put back (<see cref="AbilityScript.ResetForReuse" />), and the derived type's own by
/// <see cref="OnLaunch" />. <see cref="Clear" /> lets go of everything the last cast referenced, the derived type's by
/// <see cref="OnClear" />, so a pooled script keeps nothing alive. A derived type keeps a pooled script's wire identity
/// (its guid's wrapper) across both; the pool decides when that is safe.
/// </para>
/// <para>
/// <b>Built outside a pool</b> (by the activator, a test), a script of a pooled type is an ordinary one: its
/// <see cref="Pool" /> is null, no pool takes it back, and neither <see cref="Launch" /> nor <see cref="Clear" /> is ever
/// called on it.
/// </para>
/// </remarks>
public abstract class PooledAbilityScript : AbilityScript
{
    private IAbility _ability;
    private IUnit _caster;
    private AbilityAim _aim;

    private protected PooledAbilityScript(IAbility ability, IUnit caster, AbilityAim aim)
    {
        _ability = ability;
        _caster = caster;
        _aim = aim;
    }

    protected sealed override IUnit Caster => _caster;
    protected sealed override AbilityAim Aim => _aim;
    protected sealed override IAbility Ability => _ability;

    /// <summary>
    /// The pool that made it and takes it back, set once when it was made; null for one built anywhere else, which no pool
    /// takes. A script goes back only to its own pool, so an instance pools only what it made.
    /// </summary>
    internal AbilityScriptPool? Pool { get; init; }

    /// <summary>Handed back to its pool, until the pool hands it out again: a second return is a fault.</summary>
    internal bool Returned { get; set; }

    /// <summary>
    /// The cast's own copy of the ability, with the shape the cast was taken with: what a fan's extra lanes are built from
    /// (#1181 Part D), so a capstone taken or refunded while the cast wound up changes none of its lanes. World-side only.
    /// </summary>
    internal IAbility CastCopy => _ability;

    /// <summary>
    /// A pooled script bound to a new cast: its own copy made again from <paramref name="held" />, the caster's ability, as
    /// the cast system copies it (<see cref="GameAbility.CloneForCast" />), and every other field as a new script has it.
    /// Only for a script its pool made, whose copy is a <see cref="GameAbility" />.
    /// </summary>
    internal void Launch(GameAbility held, IUnit caster, AbilityAim aim)
    {
        ((GameAbility)_ability).CopyFrom(held);
        _caster = caster;
        _aim = aim;
        ResetForReuse();
        OnLaunch();
    }

    /// <summary>
    /// Going back to its pool: its copy cleared (no id, no metadata), no caster and no aim, and the derived type's references
    /// let go. Nothing reads it until <see cref="Launch" />.
    /// </summary>
    internal void Clear()
    {
        ((GameAbility)_ability).Clear();
        _caster = null!;
        _aim = default;
        OnClear();
    }

    /// <summary>The derived type's fields as a new script of the cast has them.</summary>
    private protected abstract void OnLaunch();

    /// <summary>The derived type's references let go, its wire identity kept.</summary>
    private protected abstract void OnClear();
}
