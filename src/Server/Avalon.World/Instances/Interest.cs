using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Instances;

namespace Avalon.World.Instances;

/// <summary>Which objects a client's world-state replication covers (#593).</summary>
public static class Interest
{
    /// <summary>
    /// Whether <paramref name="obj" /> at <paramref name="objectPosition" /> is in the view of the client
    /// whose character is <paramref name="watcher" /> at <paramref name="watcherPosition" />.
    /// </summary>
    /// <remarks>
    /// The watcher's own character is always in view. Otherwise an object enters within the radius and,
    /// once <paramref name="alreadyTracked" />, stays until beyond radius + margin, so one at the edge does
    /// not flicker. X/Z only, inclusive; a non-finite position is never in view (the finiteness rule
    /// <see cref="EffectAudience" /> uses), and a distance that overflows is +Infinity, so far.
    /// </remarks>
    public static bool IsVisible(ObjectGuid watcher, Vector3 watcherPosition, ObjectGuid obj,
        Vector3 objectPosition, bool alreadyTracked, InterestRange range)
    {
        if (obj == watcher)
        {
            return true;
        }

        if (!EffectAudience.IsFinite(watcherPosition) || !EffectAudience.IsFinite(objectPosition))
        {
            return false;
        }

        float dx = objectPosition.x - watcherPosition.x;
        float dz = objectPosition.z - watcherPosition.z;
        float reach = alreadyTracked ? range.Radius + range.Margin : range.Radius;
        return dx * dx + dz * dz <= reach * reach;
    }
}
