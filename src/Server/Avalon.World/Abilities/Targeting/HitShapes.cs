using Avalon.Common.Mathematics;

namespace Avalon.World.Abilities.Targeting;

/// <summary>
/// Pure 2D overlap tests on X/Z between a skill's shape and a unit's body circle (#164). Edges count
/// as overlaps. Heights are ignored everywhere.
/// </summary>
public static class HitShapes
{
    public static float Distance2D(Vector3 a, Vector3 b)
    {
        double dx = (double)a.x - b.x;
        double dz = (double)a.z - b.z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    public static bool CircleOverlaps(Vector3 centre, float radius, Vector3 unit, float bodyRadius) =>
        Distance2D(centre, unit) <= radius + bodyRadius;

    /// <summary>
    /// A sector from <paramref name="origin" /> along the unit vector <paramref name="direction" />,
    /// <paramref name="reach" /> long and <paramref name="arcDegrees" /> wide in full. A unit is in it
    /// when its centre is within reach plus its body, and its direction is within half the arc,
    /// widened by the angle its body spans at that distance. A body over the apex is in every
    /// direction.
    /// </summary>
    public static bool ConeOverlaps(Vector3 origin, Vector3 direction, float reach, float arcDegrees,
        Vector3 unit, float bodyRadius)
    {
        float distance = Distance2D(origin, unit);
        // Written as a negated <= so a NaN distance or reach is refused, not let through.
        if (!(distance <= reach + bodyRadius))
        {
            return false;
        }

        if (arcDegrees >= 360f || distance <= bodyRadius)
        {
            return true;
        }

        double toX = (unit.x - origin.x) / distance;
        double toZ = (unit.z - origin.z) / distance;
        double cos = Math.Clamp(toX * direction.x + toZ * direction.z, -1.0, 1.0);
        double angle = Math.Acos(cos) * 180.0 / Math.PI;
        double widening = Math.Asin(bodyRadius / distance) * 180.0 / Math.PI;

        // A tiny tolerance so a unit placed exactly on the half-arc is not lost to rounding.
        return angle <= arcDegrees / 2.0 + widening + 1e-4;
    }

    /// <summary>
    /// Whether the unit's body touches the segment. A zero-length segment is a point.
    /// </summary>
    public static bool SegmentOverlaps(Vector3 from, Vector3 to, Vector3 unit, float bodyRadius)
    {
        double t = SegmentParameter(from, to, unit);
        double px = from.x + ((double)to.x - from.x) * t - unit.x;
        double pz = from.z + ((double)to.z - from.z) * t - unit.z;
        return Math.Sqrt(px * px + pz * pz) <= bodyRadius;
    }

    /// <summary>
    /// The point of the segment nearest <paramref name="point" /> on X/Z, clamped to the segment, with
    /// its height interpolated along it. A zero-length segment gives <paramref name="from" />.
    /// </summary>
    public static Vector3 ClosestPointOnSegment(Vector3 from, Vector3 to, Vector3 point)
    {
        double t = SegmentParameter(from, to, point);
        return new Vector3(
            (float)(from.x + ((double)to.x - from.x) * t),
            (float)(from.y + ((double)to.y - from.y) * t),
            (float)(from.z + ((double)to.z - from.z) * t));
    }

    /// <summary>Where along the segment, from 0 to 1, the point nearest <paramref name="point" /> lies.</summary>
    private static double SegmentParameter(Vector3 from, Vector3 to, Vector3 point)
    {
        double sx = (double)to.x - from.x;
        double sz = (double)to.z - from.z;
        double lengthSquared = sx * sx + sz * sz;

        return lengthSquared <= 0.0
            ? 0.0
            : Math.Clamp((((double)point.x - from.x) * sx + ((double)point.z - from.z) * sz) / lengthSquared, 0.0, 1.0);
    }
}
