using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abilities;
using Avalon.World.Abilities;
using Avalon.World.Abilities.Targeting;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;

namespace Avalon.World.Scripts.Abilities;

/// <summary>
/// A circle on the caster, or on the aim point clamped to Reach and pulled back from any wall between
/// (#164). Resolves once, when it fires: every qualifying unit whose body overlaps the radius, nearest
/// the centre first. The caster's position is read when it fires, never when the script is built.
/// </summary>
public sealed class CircleAbilityScript(IAbility ability, IUnit caster, AbilityAim aim, IAbilityArena arena)
    : AbilityScript(ability, caster, aim)
{
    public override Vector3 Position { get; set; }
    public override Vector3 Velocity { get; set; }
    public override Vector3 Orientation { get; set; }
    public override ObjectGuid Guid { get; set; }
    public override object State { get; set; } = SpellState.Executing;

    protected override bool ShouldRun() => true;

    public override void Prepare()
    {
        Guid = new ObjectGuid(ObjectType.Spell, IObject.GenerateId());
        AbilityMetadata meta = Ability.Metadata;

        Vector3 centre = meta.Anchor == AbilityAnchor.AimPoint ? AimPointCentre(meta.Reach) : Caster.Position;
        Position = centre;

        arena.BroadcastAbilityFired(Caster, Ability, Caster.Position, direction: null, centre);
        AbilityEffect.ApplyToAll(arena, Caster, Ability, arena.Hits.InCircle(centre, meta.Radius));

        State = SpellState.Finished;
    }

    private Vector3 AimPointCentre(float reach)
    {
        Vector3 from = Caster.Position;
        Vector3 direction = Aim.DirectionFrom(from);
        float distance = Aim.Point is { } point ? Math.Min(HitShapes.Distance2D(from, point), reach) : 0f;
        var clamped = new Vector3(from.x + direction.x * distance, from.y, from.z + direction.z * distance);

        // A blast cannot land behind a wall: the centre stops where the walkable ray does.
        return arena.GetNavigatorForPosition(from).RaycastWalkable(from, clamped);
    }
}
