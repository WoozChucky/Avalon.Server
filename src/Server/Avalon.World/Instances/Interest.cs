using System.Runtime.CompilerServices;
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

        Measure(watcherPosition, objectPosition.x, objectPosition.z, range, out bool withinAdd, out bool withinKeep);

        // A tracked object is measured against radius + margin and any other against the radius, so where both say
        // the same the tracked set cannot change the answer.
        if (withinAdd == withinKeep)
        {
            return withinAdd;
        }

        return tracked.Contains(obj) ? withinKeep : withinAdd;
    }

    /// <summary>
    /// Whether the distance alone decides <see cref="IsVisible{TTracked}" /> for an object at (<paramref name="x" />,
    /// <paramref name="z" />) that is not the watcher, both positions finite (#982): true, with the answer in
    /// <paramref name="inView" />, unless the object is in the margin, where whether the client already holds it
    /// decides. The same measure on the same floats as <see cref="IsVisible{TTracked}" />, so a spatial index's
    /// candidate is decided on the index's copy of its X/Z without its entity being read. The caller answers the two
    /// rules before the distance itself: the watcher's own character, and a position that is not finite.
    /// </summary>
    internal static bool DecidedByDistance(Vector3 watcherPosition, float x, float z, InterestRange range,
        out bool inView)
    {
        Measure(watcherPosition, x, z, range, out bool withinAdd, out bool withinKeep);
        inView = withinAdd;
        return withinAdd == withinKeep;
    }

    /// <summary>
    /// The farthest on X/Z an object can be and still be in view, tracked or not (#982): the larger of the radius and
    /// radius + margin, as <see cref="IsVisible{TTracked}" /> squares them. Not a number when either is not.
    /// </summary>
    internal static float Reach(InterestRange range) =>
        MathF.Max(MathF.Abs(range.Radius), MathF.Abs(range.Radius + range.Margin));

    // The one distance measure of the rule, X/Z, inclusive, against the radius and against radius + margin.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Measure(Vector3 watcherPosition, float x, float z, InterestRange range,
        out bool withinAdd, out bool withinKeep)
    {
        float dx = x - watcherPosition.x;
        float dz = z - watcherPosition.z;
        float distanceSquared = dx * dx + dz * dz;
        float addReach = range.Radius;
        float keepReach = range.Radius + range.Margin;
        withinAdd = distanceSquared <= addReach * addReach;
        withinKeep = distanceSquared <= keepReach * keepReach;
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
