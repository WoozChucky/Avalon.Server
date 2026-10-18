namespace Avalon.ChunkGen;

/// <summary>
/// A forest piece's paths: a worn dirt strip from the midpoint of every exit side to the piece's meeting point, laid
/// as <see cref="Segment" /> pieces end to end and ending under a <see cref="Hub" /> there. Every strip is
/// <see cref="Width" /> wide and its first segment runs straight in from the side, so it meets its neighbour's at
/// the shared edge whatever either chunk's rotation. After the first segment a path wanders a little, bending at most
/// <see cref="MaxBend" /> degrees a segment; each segment starts <see cref="Overlap" /> back over the one before, which
/// covers the wedge a bend opens at the outer edge, and consecutive segments alternate between two heights
/// (<see cref="Lift" />), so the overlap never draws two strips at one height.
/// </summary>
public static class ForestPaths
{
    /// <summary>The dirt strip's width, the same for every piece: the art's avalon_path_dirt_2m is exactly this wide at both ends.</summary>
    public const float Width = 2.8f;

    /// <summary>A segment's length along its yaw (the model's +Z).</summary>
    public const float SegmentLength = 2f;

    public const string Segment = "avalon_path_dirt_2m";

    public const string Hub = "avalon_path_dirt_hub";

    /// <summary>The hub patch's radius, about 2.55 m in the art, its top 0.025 m: above every strip.</summary>
    public const float HubRadius = 2.5f;

    /// <summary>A path's last end lies within this of the meeting point, so its corners (1.4 m to either side) are under the hub.</summary>
    public const float HubReach = 1.6f;

    /// <summary>The most a path bends from one segment to the next, in degrees.</summary>
    public const int MaxBend = 6;

    /// <summary>The most a path's middle strays from the straight line, in metres.</summary>
    public const float Wander = 1f;

    /// <summary>The height every other segment is raised by, above its neighbours and below the hub's top.</summary>
    public const float Lift = 0.002f;

    /// <summary>
    /// How far each segment starts back over the one before it: at least half the width times sin <see cref="MaxBend" />
    /// (0.146 m), the depth of the wedge a bend opens at the strip's outer edge.
    /// </summary>
    public const float Overlap = 0.2f;

    /// <summary>How far a path advances a segment.</summary>
    public const float Advance = SegmentLength - Overlap;

    /// <summary>The ends of a segment's centre line, its origin ± half its length along its yaw, scaled.</summary>
    public static ((float X, float Z) A, (float X, float Z) B) Ends(DecorItem segment)
    {
        (float fx, float fz) = Yaw.Forward(segment.Yaw);
        float half = SegmentLength / 2f * segment.Scale;
        return ((segment.X - fx * half, segment.Z - fz * half), (segment.X + fx * half, segment.Z + fz * half));
    }

    /// <summary>
    /// The path from <paramref name="exit" /> to the frame's meeting point: the first segment straight in from the
    /// exit's midpoint, then each starting <see cref="Overlap" /> back over the last, turned by a whole degree, at most
    /// <see cref="MaxBend" />, to end nearest a waypoint beside the straight line's middle (<see cref="Wander" /> off it,
    /// drawn from <paramref name="rng" />), then the meeting point, until an end lies within <see cref="HubReach" /> of
    /// it. Consecutive segments alternate heights, a path starting at <paramref name="parity" />'s. A path that cannot
    /// get there is refused (InvalidOperationException naming the frame and the exit).
    /// </summary>
    internal static List<DecorItem> Lay(ForestFrame frame, ForestExit exit, int parity, Random rng)
    {
        float meetX = frame.MeetX, meetZ = frame.MeetZ;
        float offset = ((float)rng.NextDouble() * 2f - 1f) * Wander;
        // The waypoint: the straight line's middle, moved across it (the line's left-hand normal) by the offset.
        float lineX = meetX - exit.X, lineZ = meetZ - exit.Z;
        float length = MathF.Sqrt(lineX * lineX + lineZ * lineZ);
        (float wayX, float wayZ) = (exit.X + lineX / 2f - lineZ / length * offset, exit.Z + lineZ / 2f + lineX / length * offset);

        List<DecorItem> segments = [];
        float x = exit.X, z = exit.Z;
        int yaw = exit.InwardYaw;
        bool passedWaypoint = false;
        int limit = (int)(length / Advance) * 2 + 8;
        for (int step = 0; step < limit; step++)
        {
            // (x, z) is the last segment's end; the next starts Overlap back from it along its own yaw.
            float back = 0f;
            if (step > 0)
            {
                (float targetX, float targetZ) = passedWaypoint ? (meetX, meetZ) : (wayX, wayZ);
                yaw = Steer(x, z, yaw, targetX, targetZ);
                back = Overlap;
            }
            (float fx, float fz) = Yaw.Forward(yaw);
            float startX = x - fx * back, startZ = z - fz * back;
            segments.Add(new DecorItem("", Segment, startX + fx * SegmentLength / 2f, (step + parity) % 2 * Lift,
                startZ + fz * SegmentLength / 2f, Yaw.Normal(yaw)));
            x = startX + fx * SegmentLength;
            z = startZ + fz * SegmentLength;
            if (!passedWaypoint && Distance(x, z, wayX, wayZ) <= SegmentLength * 1.5f) passedWaypoint = true;
            if (Distance(x, z, meetX, meetZ) <= HubReach) return segments;
        }
        throw new InvalidOperationException(
            $"{frame.Name}: the path from the {exit.Side} exit does not reach the meeting point ({ForestText.N(meetX)}, {ForestText.N(meetZ)})");
    }

    /// <summary>The whole-degree yaw within <see cref="MaxBend" /> of <paramref name="yaw" /> whose next segment ends nearest the target; the smaller turn on a tie.</summary>
    private static int Steer(float x, float z, int yaw, float targetX, float targetZ)
    {
        int best = yaw;
        float bestDistance = float.MaxValue;
        for (int turn = 0; turn <= MaxBend; turn++)
        {
            foreach (int candidate in turn == 0 ? [yaw] : new[] { yaw - turn, yaw + turn })
            {
                (float fx, float fz) = Yaw.Forward(candidate);
                float d = Distance(x + fx * Advance, z + fz * Advance, targetX, targetZ);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = candidate;
                }
            }
        }
        return best;
    }

    internal static float Distance(float ax, float az, float bx, float bz) => MathF.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

    /// <summary>Metres from (px, pz) to the segment from a to b.</summary>
    internal static float DistanceToSegment(float px, float pz, (float X, float Z) a, (float X, float Z) b)
    {
        float dx = b.X - a.X, dz = b.Z - a.Z;
        float lengthSquared = dx * dx + dz * dz;
        float t = lengthSquared == 0f ? 0f : Math.Clamp(((px - a.X) * dx + (pz - a.Z) * dz) / lengthSquared, 0f, 1f);
        return Distance(a.X + t * dx, a.Z + t * dz, px, pz);
    }
}
