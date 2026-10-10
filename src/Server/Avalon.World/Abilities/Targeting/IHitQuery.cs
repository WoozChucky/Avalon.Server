using Avalon.Common.Mathematics;
using Avalon.World.Public.Units;

namespace Avalon.World.Abilities.Targeting;

/// <summary>
/// The living units whose body circle overlaps a shape, on X/Z, from an instance's characters and
/// creatures (#164). Each list is nearest-first by distance from the shape's origin (a circle's
/// centre, a cone's apex, a segment's start) to the unit's centre, ties broken by raw guid. A dead
/// unit never appears. World-side on purpose: it is not part of the modding API.
/// <para>
/// A result is the query's own list, lent until the instance's next tick: read it within the tick it was asked in, never
/// write to it, and never keep it (copy what must outlive the tick). Another query in the same tick returns a list of its
/// own, so a result being read is never overwritten by a query asked meanwhile.
/// </para>
/// </summary>
public interface IHitQuery
{
    IReadOnlyList<IUnit> InCircle(Vector3 centre, float radius);

    /// <param name="direction">A unit vector on X/Z.</param>
    /// <param name="arcDegrees">The full arc, above 0 and at most 360.</param>
    IReadOnlyList<IUnit> InCone(Vector3 origin, Vector3 direction, float reach, float arcDegrees);

    IReadOnlyList<IUnit> OnSegment(Vector3 from, Vector3 to);
}
