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
        Vector3 objectPosition, bool alreadyTracked, InterestRange range) =>
        IsVisible(watcher, watcherPosition, obj, objectPosition, range, new Known(alreadyTracked));

    /// <summary>
    /// <see cref="IsVisible(ObjectGuid, Vector3, ObjectGuid, Vector3, bool, InterestRange)" />, asking
    /// <paramref name="tracked" /> whether the object is already tracked only when the answer depends on it (#982):
    /// an object within the radius is in view, and one beyond radius + margin is not, whatever the client holds, so
    /// only one between the two (the margin) asks. Most of a view's candidates are far, and the question is a lookup.
    /// </summary>
    internal static bool IsVisible<TTracked>(ObjectGuid watcher, Vector3 watcherPosition, ObjectGuid obj,
        Vector3 objectPosition, InterestRange range, TTracked tracked)
        where TTracked : struct, ITrackedSet
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
        float distanceSquared = dx * dx + dz * dz;
        float addReach = range.Radius;
        float keepReach = range.Radius + range.Margin;
        bool withinAdd = distanceSquared <= addReach * addReach;
        bool withinKeep = distanceSquared <= keepReach * keepReach;

        // A tracked object is measured against radius + margin and any other against the radius, so where both say
        // the same the tracked set cannot change the answer.
        if (withinAdd == withinKeep)
        {
            return withinAdd;
        }

        return tracked.Contains(obj) ? withinKeep : withinAdd;
    }

    /// <summary>The objects a client's view already holds, asked only for one in the margin.</summary>
    internal interface ITrackedSet
    {
        bool Contains(ObjectGuid obj);
    }

    /// <summary>An answer already known.</summary>
    private readonly struct Known(bool tracked) : ITrackedSet
    {
        public bool Contains(ObjectGuid obj) => tracked;
    }
}
