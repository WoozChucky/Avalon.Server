using System.Runtime.CompilerServices;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Abilities;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// A projectile the state encoding test can find again (#980): it flies flat from where its cast started toward the aim
/// at the ability's speed, for its reach, then stops and finishes, as the shipped projectile does when it hits nothing;
/// and every one built is listed by the instance it flies in (<see cref="In" />), which the shipped one is not. Built
/// by the real cast system under the shipped projectile's script name.
/// </summary>
public sealed class FlyingProjectileScript(IAbility ability, IUnit caster, AbilityAim aim, IAbilityArena arena)
    : AbilityScript(ability, caster, aim)
{
    private static readonly ConditionalWeakTable<IAbilityArena, List<FlyingProjectileScript>> s_flying = new();

    private Vector3 _direction;
    private float _travelled;

    public override object State { get; set; } = SpellState.Executing;
    public override Vector3 Position { get; set; }
    public override Vector3 Velocity { get; set; }
    public override Vector3 Orientation { get; set; }
    public override ObjectGuid Guid { get; set; }

    /// <summary>Every projectile of this script built in <paramref name="instance" />, oldest first.</summary>
    public static IReadOnlyList<FlyingProjectileScript> In(IAbilityArena instance) =>
        s_flying.TryGetValue(instance, out List<FlyingProjectileScript>? flying) ? flying : [];

    public override void Prepare()
    {
        Guid = new ObjectGuid(ObjectType.SpellProjectile, IObject.GenerateId());
        _direction = Aim.DirectionFrom(Origin);
        Velocity = _direction * Ability.Metadata.ProjectileSpeed;
        Orientation = new Vector3(0f, MathF.Atan2(_direction.x, _direction.z) * Mathf.Rad2Deg, 0f);
        Position = Origin + new Vector3(0f, 0.5f, 0f);
        s_flying.GetOrCreateValue(arena).Add(this);
    }

    public override void Update(TimeSpan deltaTime)
    {
        if (State is SpellState.Finished)
            return;

        AbilityMetadata meta = Ability.Metadata;
        float step = Math.Max(Math.Min(meta.ProjectileSpeed * (float)deltaTime.TotalSeconds, meta.Reach - _travelled), 0f);
        Position += _direction * step;
        _travelled += step;
        _dirtyFields |= GameEntityFields.Position;

        if (_travelled >= meta.Reach - 0.001f)
        {
            State = SpellState.Finished;
            Velocity = Vector3.zero;
            _dirtyFields |= GameEntityFields.Velocity;
        }
    }

    protected override bool ShouldRun() => true;
}
