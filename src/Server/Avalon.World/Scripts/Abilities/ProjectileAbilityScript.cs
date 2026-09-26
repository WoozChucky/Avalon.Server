using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Abilities;
using Avalon.World.Abilities.Targeting;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;

namespace Avalon.World.Scripts.Abilities;

/// <summary>
/// A projectile (#164): a world object spawned at the caster, flying flat toward the aim point at
/// ProjectileSpeed (metres per second, the #424 velocity rule). Each tick it sweeps its step, capped at
/// the Reach left and cut by the navmesh ray, against unit bodies, nearest first: without Pierce it
/// ends on the first qualifying hit, with Pierce it hits each unit once and flies on. It ends at Reach,
/// at a wall, or on a first hit, and the instance then despawns it. No target, no homing. The caster's
/// position is read when it fires, never when the script is built.
/// </summary>
public sealed class ProjectileAbilityScript(IAbility ability, IUnit caster, AbilityAim aim, IAbilityArena arena)
    : AbilityScript(ability, caster, aim)
{
    /// <summary>Where the projectile is drawn above the ground it flies along. Hit tests ignore height.</summary>
    public static readonly Vector3 HeightOffset = new(0f, 0.5f, 0f);

    private const float Epsilon = 0.001f;

    private readonly HashSet<ObjectGuid> _hit = [];
    private Vector3 _ground;
    private Vector3 _direction;
    private float _travelled;

    public override Vector3 Position { get; set; }
    public override Vector3 Velocity { get; set; }
    public override Vector3 Orientation { get; set; }
    public override ObjectGuid Guid { get; set; }
    public override object State { get; set; } = SpellState.Executing;

    protected override bool ShouldRun() => true;

    public override void Prepare()
    {
        Guid = new ObjectGuid(ObjectType.SpellProjectile, IObject.GenerateId());
        _ground = Caster.Position;
        _direction = Aim.DirectionFrom(_ground);
        Velocity = _direction * Ability.Metadata.ProjectileSpeed;
        Orientation = new Vector3(0f, MathF.Atan2(_direction.x, _direction.z) * Mathf.Rad2Deg, 0f);
        Position = _ground + HeightOffset;
    }

    public override void Update(TimeSpan deltaTime)
    {
        if (State is SpellState.Finished)
        {
            return;
        }

        AbilityMetadata meta = Ability.Metadata;
        float step = Math.Min(meta.ProjectileSpeed * (float)deltaTime.TotalSeconds, meta.Reach - _travelled);
        step = Math.Max(step, 0f);

        Vector3 wanted = new(_ground.x + _direction.x * step, _ground.y, _ground.z + _direction.z * step);
        Vector3 end = step > 0f ? arena.GetNavigatorForPosition(_ground).RaycastWalkable(_ground, wanted) : _ground;
        float moved = HitShapes.Distance2D(_ground, end);
        bool blocked = moved < step - Epsilon;

        foreach (IUnit unit in arena.Hits.OnSegment(_ground, end))
        {
            if (_hit.Contains(unit.Guid) || !AbilityEffect.Qualifies(arena, Caster, Ability, unit))
            {
                continue;
            }

            _hit.Add(unit.Guid);
            AbilityEffect.Apply(arena, Caster, Ability, unit);

            if (!meta.Pierce)
            {
                MoveTo(end, moved);
                Finish();
                return;
            }
        }

        MoveTo(end, moved);

        if (blocked || _travelled >= meta.Reach - Epsilon)
        {
            Finish();
        }
    }

    private void MoveTo(Vector3 ground, float moved)
    {
        _ground = ground;
        _travelled += moved;
        Position = _ground + HeightOffset;
        _dirtyFields |= GameEntityFields.Position;
    }

    private void Finish()
    {
        State = SpellState.Finished;
        Velocity = Vector3.zero;
        _dirtyFields |= GameEntityFields.Velocity;
    }
}
