using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Abilities;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;

namespace Avalon.World.Scripts.Abilities;

/// <summary>
/// What a circle and a cone share (#1238): each resolves once, inside its <c>Prepare</c>, and is finished when that
/// returns, so it never runs, is never a world object and its guid reaches no view. Pooled per instance as projectiles are
/// (<see cref="AbilityScriptPool" />), so a warm circle or cone cast allocates nothing, and free again as it fires.
/// </summary>
/// <remarks>
/// <para>
/// <b>The reset contract.</b> <see cref="PooledAbilityScript.Launch" /> leaves it as the public constructor would: at rest
/// at the origin, no guid until it is prepared, executing. <see cref="PooledAbilityScript.Clear" /> leaves it finished,
/// holding nothing of its cast. It keeps no hit set: a circle or a cone hits every unit its one query answers, once.
/// </para>
/// <para>
/// <b>Its guid.</b> One wrapper of its own, made at its first <see cref="Prepare" />, which writes a new id into it every
/// cast: an id is never reused. Nothing outside the script reads it, since the fired broadcast names the caster.
/// </para>
/// <para>
/// <b>Its state</b> is one of two boxes every circle and cone shares, since <see cref="State" /> is an object: a cast
/// boxes nothing.
/// </para>
/// <para>
/// <b>No constructor services.</b> A pooled type is built by the factory it registered with
/// (<c>InstanceAbilityCastSystem.NewPool</c>), which passes the four cast arguments and nothing from the container, and the
/// pool refuses, as the cast system is built, a circle or a cone with any constructor besides its one public constructor
/// taking those four (<see cref="AbilityScriptPool.Pools{T}" />).
/// </para>
/// </remarks>
public abstract class AreaAbilityScript : PooledAbilityScript
{
    private static readonly object s_executing = SpellState.Executing;
    private static readonly object s_finished = SpellState.Finished;

    // The wrapper this script's ids are written into, its own: Guid is set to it, and only it is ever changed here.
    private ObjectGuid? _wrapper;

    private protected AreaAbilityScript(IAbility ability, IUnit caster, AbilityAim aim, IAbilityArena arena)
        : base(ability, caster, aim)
    {
        Arena = arena;
    }

    /// <summary>The instance it resolves in, its pool's: the same for every cast it is launched for.</summary>
    private protected IAbilityArena Arena { get; }

    public override Vector3 Position { get; set; }
    public override Vector3 Velocity { get; set; }
    public override Vector3 Orientation { get; set; }
    public override ObjectGuid Guid { get; set; }
    public override object State { get; set; } = s_executing;

    protected override bool ShouldRun() => true;

    /// <summary>A new id in the script's own wrapper, then the shape resolved and hit (<see cref="Resolve" />), then finished.</summary>
    public sealed override void Prepare()
    {
        _wrapper ??= new ObjectGuid();
        _wrapper.Set(ObjectType.Spell, IObject.GenerateId());
        Guid = _wrapper;
        Resolve();
        State = s_finished;
    }

    /// <summary>Resolves the shape from the cast's origin, tells it fired, and hits every unit in it.</summary>
    private protected abstract void Resolve();

    private protected override void OnLaunch()
    {
        Position = Vector3.zero;
        Velocity = Vector3.zero;
        Orientation = Vector3.zero;
        Guid = null!;
        State = s_executing;
    }

    private protected override void OnClear() => State = s_finished;
}
