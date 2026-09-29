using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abilities;
using Avalon.World.Abilities.Targeting;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Maps;

namespace Avalon.World.Abilities;

/// <summary>
/// Where an ability lands (#648), resolved once from its row, its aim and where its caster stood when the
/// cast started. The shape scripts fire with exactly what <see cref="Resolve" /> answers, and a cast-time
/// cast's start broadcast carries the same answer, so the telegraph a client draws is what hits.
/// </summary>
/// <param name="Direction">A cone's or a projectile's direction, a unit vector on X/Z; null for a circle.</param>
/// <param name="Centre">A circle's centre; null for a cone and a projectile.</param>
/// <param name="Reach">A cone's length, or a projectile lane's up to where the walkable ray stops; 0 for a circle.</param>
public readonly record struct AbilityFootprint(
    AbilityShape Shape,
    Vector3 Origin,
    Vector3? Direction,
    Vector3? Centre,
    float Radius,
    float Reach,
    float ArcDegrees)
{
    /// <summary>
    /// The footprint of <paramref name="meta" /> cast from <paramref name="origin" /> at <paramref name="aim" />,
    /// by the row's shape. Null for a shape this does not know. The navmesh is fixed for an instance's life, so
    /// the same inputs always give the same footprint.
    /// </summary>
    public static AbilityFootprint? Resolve(AbilityMetadata meta, AbilityAim aim, Vector3 origin, IMapNavigator navigator) =>
        Resolve(meta.Shape, meta, aim, origin, navigator);

    /// <summary>
    /// As the other overload, as <paramref name="shape" />: what a shape script fires with. The catalog refuses a
    /// row whose script is not its shape's, so this and the row's own shape agree.
    /// </summary>
    public static AbilityFootprint? Resolve(AbilityShape shape, AbilityMetadata meta, AbilityAim aim, Vector3 origin,
        IMapNavigator navigator) =>
        shape switch
        {
            AbilityShape.Circle => new AbilityFootprint(AbilityShape.Circle, origin, null,
                meta.Anchor == AbilityAnchor.AimPoint ? AimPointCentre(aim, origin, meta.Reach, navigator) : origin,
                meta.Radius, 0f, 0f),
            AbilityShape.Cone => new AbilityFootprint(AbilityShape.Cone, origin,
                meta.AimMode == AbilityAimMode.Movement ? aim.Facing : aim.DirectionFrom(origin), null,
                0f, meta.Reach, meta.ArcDegrees),
            AbilityShape.Projectile => Lane(meta, aim, origin, navigator),
            _ => null,
        };

    public AbilityFootprintDto ToDto() => AbilityFootprintDto.Create(Shape, Origin, Direction, Centre, Radius, Reach, ArcDegrees);

    /// <summary>The aim point clamped to Reach, stopped where the walkable ray does: a blast cannot land behind a wall.</summary>
    private static Vector3 AimPointCentre(AbilityAim aim, Vector3 origin, float reach, IMapNavigator navigator)
    {
        Vector3 direction = aim.DirectionFrom(origin);
        float distance = aim.Point is { } point ? Math.Min(HitShapes.Distance2D(origin, point), reach) : 0f;
        var clamped = new Vector3(origin.x + direction.x * distance, origin.y, origin.z + direction.z * distance);
        return navigator.RaycastWalkable(origin, clamped);
    }

    /// <summary>A projectile's lane: toward the aim point (or along the facing), for Reach, cut where the walkable ray stops.</summary>
    private static AbilityFootprint Lane(AbilityMetadata meta, AbilityAim aim, Vector3 origin, IMapNavigator navigator)
    {
        Vector3 direction = aim.DirectionFrom(origin);
        var far = new Vector3(origin.x + direction.x * meta.Reach, origin.y, origin.z + direction.z * meta.Reach);
        float reach = meta.Reach > 0f ? HitShapes.Distance2D(origin, navigator.RaycastWalkable(origin, far)) : 0f;
        return new AbilityFootprint(AbilityShape.Projectile, origin, direction, null, 0f, reach, 0f);
    }
}
