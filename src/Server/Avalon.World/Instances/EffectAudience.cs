using Avalon.Common;
using Avalon.Common.Mathematics;

namespace Avalon.World.Instances;

/// <summary>
/// Who hears a one-shot effect broadcast (#532): a connection whose character is involved in the
/// effect, or stands within the radius of one of its points on X/Z, the plane the hit shapes use.
/// </summary>
public static class EffectAudience
{
    /// <summary>
    /// Whether the watcher <paramref name="watcher" />, standing at <paramref name="watcherPosition" />,
    /// receives an effect involving <paramref name="involved" /> (and <paramref name="alsoInvolved" />)
    /// at <paramref name="point" /> (and <paramref name="alsoPoint" />).
    /// </summary>
    /// <remarks>
    /// The boundary is inclusive. A non-finite position, the watcher's or a point's, is never near, but
    /// an involved watcher receives the effect whatever its position. Height is ignored, so a watcher
    /// directly above or below the effect is near it.
    /// </remarks>
    public static bool Receives(ObjectGuid watcher, Vector3 watcherPosition, float radius,
        ObjectGuid involved, ObjectGuid? alsoInvolved, Vector3 point, Vector3? alsoPoint)
    {
        if (watcher == involved || (alsoInvolved is not null && watcher == alsoInvolved))
        {
            return true;
        }

        if (!IsFinite(watcherPosition))
        {
            return false;
        }

        float radiusSquared = radius * radius;
        return IsNear(watcherPosition, point, radiusSquared)
               || (alsoPoint.HasValue && IsNear(watcherPosition, alsoPoint.Value, radiusSquared));
    }

    private static bool IsNear(Vector3 watcher, Vector3 point, float radiusSquared)
    {
        if (!IsFinite(point))
        {
            return false;
        }

        float dx = watcher.x - point.x;
        float dz = watcher.z - point.z;
        return dx * dx + dz * dz <= radiusSquared;
    }

    internal static bool IsFinite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
}
