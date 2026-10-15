using System.Diagnostics;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Units;

namespace Avalon.World.Abilities.Targeting;

/// <summary>
/// <see cref="IHitQuery" /> over an instance's characters and creatures. Tick thread only, like the dictionaries and the
/// spatial grid it reads.
/// </summary>
/// <remarks>
/// <para>
/// <b>Candidates (#982).</b> Built over the instance's <see cref="SpatialGrid" />, a query visits only the cells of the
/// shape's box on X/Z (<see cref="Shape.TryBound" />: its extent widened by the largest body radius the grid has indexed,
/// and by more than any rounding of the exact test), and the grid's lists of entities in no cell or not indexed, which
/// it visits whole. A cell's entry outside the box is left on the grid's copy of its X/Z, which is the entity's own (every
/// position write reaches the grid through the concrete setters), without the unit being read; every other candidate is
/// tested on the unit as before: alive, then the exact overlap. No unit outside the box can overlap the shape, so the
/// hits are the full scan's, and the nearest-first, raw-guid order makes the list the same whatever order they were
/// found in. A box that is not finite (a position, a size or the largest radius not finite) scans every member, as does
/// a query built over the dictionaries alone (a test's).
/// </para>
/// <para>
/// A query allocates nothing once its lists exist (#880): the dictionaries are walked with their struct enumerators, the
/// grid with a struct visitor, the shape is tested and the hits sorted without a new delegate or closure, and each result
/// is a list the query keeps and reuses. A list handed out is not reused before <see cref="Recycle" />, which the instance
/// calls at the start of each tick: every result stays valid for the rest of the tick it was asked in, however many
/// queries follow, a query asked while another's result is being walked (a hit that leads to another cast) included. A
/// query nobody recycles (a test's) keeps every list it handed out.
/// </para>
/// </remarks>
public sealed class UnitHitQuery(
    Dictionary<ObjectGuid, ICharacter> characters,
    Dictionary<ObjectGuid, ICreature> creatures) : IHitQuery
{
    private readonly List<List<IUnit>> _lists = [];
    private readonly NearestFirst _nearestFirst = new();
    private int _handedOut;
    private SpatialGrid? _members;

    /// <summary>
    /// A query over <paramref name="members" />, the instance's spatial grid, which holds exactly the entities of
    /// <paramref name="characters" /> and <paramref name="creatures" /> (#982).
    /// </summary>
    internal UnitHitQuery(
        Dictionary<ObjectGuid, ICharacter> characters,
        Dictionary<ObjectGuid, ICreature> creatures,
        SpatialGrid members) : this(characters, creatures)
    {
        _members = members;
    }

    /// <summary>
    /// What the queries since this one was built did, by shape (circle, cone, segment): for measuring the grid alone
    /// (#982); read by name by the <c>hit-candidates</c> harness.
    /// </summary>
    internal HitQueryCounts[] Counts { get; } = new HitQueryCounts[3];

    public IReadOnlyList<IUnit> InCircle(Vector3 centre, float radius) =>
        Collect(centre, new Shape(ShapeKind.Circle, centre, default, radius, 0f));

    public IReadOnlyList<IUnit> InCone(Vector3 origin, Vector3 direction, float reach, float arcDegrees) =>
        Collect(origin, new Shape(ShapeKind.Cone, origin, direction, reach, arcDegrees));

    public IReadOnlyList<IUnit> OnSegment(Vector3 from, Vector3 to) =>
        Collect(from, new Shape(ShapeKind.Segment, from, to, 0f, 0f));

    /// <summary>
    /// A character is dead while <c>IsDead</c>. A creature has no dead flag: it is dead at 0 health,
    /// and its corpse stays in the instance until the corpse remover takes it.
    /// </summary>
    public static bool IsAlive(IUnit unit) =>
        unit is ICharacter character ? !character.IsDead : unit.CurrentHealth > 0;

    /// <summary>
    /// Every list handed out may be reused from now on: each is emptied, so none holds a unit past the tick it was asked
    /// in. The instance calls it at the start of its tick, when no result of the last one is still being read.
    /// </summary>
    public void Recycle()
    {
        for (int i = 0; i < _handedOut; i++)
            _lists[i].Clear();
        _handedOut = 0;
    }

    /// <summary>
    /// Every query scans every member from now on, as before #982, rather than the grid's cells: the reference the grid
    /// is compared against. For tests and harnesses only (<c>MapInstance.FindHitsByFullScan</c>).
    /// </summary>
    internal void ScanEveryMember() => _members = null;

    private List<IUnit> Collect(Vector3 origin, in Shape shape)
    {
        List<IUnit> hits = NextList();
        int members = characters.Count + creatures.Count;
        int visited, examined;

        if (_members is not null && shape.TryBound(_members.LargestBodyRadius, out HitBox box))
        {
            var candidates = new HitCandidates(shape, box, hits, _members.LargestBodyRadius);
            _members.VisitBox(box.MinX, box.MinZ, box.MaxX, box.MaxZ, ref candidates);
            visited = candidates.Visited;
            examined = candidates.Examined;
        }
        else
        {
            foreach (ICharacter character in characters.Values)
            {
                if (IsAlive(character) && shape.Overlaps(character))
                    hits.Add(character);
            }

            foreach (ICreature creature in creatures.Values)
            {
                if (IsAlive(creature) && shape.Overlaps(creature))
                    hits.Add(creature);
            }

            visited = examined = members;
        }

        ref HitQueryCounts counts = ref Counts[(int)shape.Kind];
        counts.Queries++;
        counts.Members += members;
        counts.Visited += visited;
        counts.Examined += examined;
        counts.Hits += hits.Count;

        _nearestFirst.Origin = origin;
        hits.Sort(_nearestFirst.Comparison);

        return hits;
    }

    /// <summary>A list not handed out since the last <see cref="Recycle" /> (which emptied it); a new one when every one is.</summary>
    private List<IUnit> NextList()
    {
        if (_handedOut == _lists.Count)
            _lists.Add([]);

        return _lists[_handedOut++];
    }

    private enum ShapeKind
    {
        Circle,
        Cone,
        Segment,
    }

    /// <summary>
    /// One query's shape: a circle (<c>A</c> its centre, <c>Size</c> its radius), a cone (<c>A</c> its apex,
    /// <c>B</c> its direction, <c>Size</c> its reach, <c>Arc</c> its full arc) or a segment (<c>A</c> to <c>B</c>).
    /// </summary>
    private readonly record struct Shape(ShapeKind Kind, Vector3 A, Vector3 B, float Size, float Arc)
    {
        // The box's margins beyond what the exact test can accept: a part in 1,024 of its extent, plus 1/1,024 m, plus a
        // part in 2^40 of the shape's largest coordinate. The exact tests measure in double and compare in float, so
        // what they accept is within a few parts in 2^24 of the extent; the box is worked out in double, whose rounding
        // is a part in 2^53 of the coordinates, then rounded outwards to floats.
        private const double RelativeMargin = 1.0 / 1024.0;
        private const double AbsoluteMargin = 1.0 / 1024.0;
        private const double CoordinateMargin = 1.0 / (1L << 40);

        public bool Overlaps(IUnit unit) => Kind switch
        {
            ShapeKind.Circle => HitShapes.CircleOverlaps(A, Size, unit.Position, unit.BodyRadius),
            ShapeKind.Cone => HitShapes.ConeOverlaps(A, B, Size, Arc, unit.Position, unit.BodyRadius),
            _ => HitShapes.SegmentOverlaps(A, B, unit.Position, unit.BodyRadius),
        };

        /// <summary>
        /// The box on X/Z outside which no unit whose body radius is at most <paramref name="largestBodyRadius" /> can
        /// overlap the shape (#982); false when it is not finite, for the query to scan every member.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A circle overlaps a unit only when the distance between their centres, measured in double and rounded to a
        /// float, is at most the radius plus the unit's body radius, added in float (<see cref="HitShapes.CircleOverlaps" />);
        /// a cone first requires the same of its reach (<see cref="HitShapes.ConeOverlaps" />), whatever its direction or
        /// arc. Either way the unit's centre is within |size| + body radius of the shape's centre, times at most
        /// 1 + 2^-22 for the two roundings to float, on X and on Z alike. A segment touches a unit only when the point of
        /// the segment nearest its centre is within its body radius (<see cref="HitShapes.SegmentOverlaps" />), and that
        /// point is between the ends on each axis, so the centre is within the body radius of the segment's own box, give
        /// or take the double's rounding. A negative size makes its shape no larger than its absolute value does.
        /// </para>
        /// <para>
        /// A NaN or infinite position, size or radius makes a bound NaN or infinite, and then nothing is pruned. A finite
        /// box larger than the layout is clamped to the grid's edge cells, so it visits at most every cell, which is what
        /// a full scan costs.
        /// </para>
        /// </remarks>
        public bool TryBound(float largestBodyRadius, out HitBox box)
        {
            double minX, maxX, minZ, maxZ, extent;
            if (Kind == ShapeKind.Segment)
            {
                minX = Math.Min(A.x, B.x);
                maxX = Math.Max(A.x, B.x);
                minZ = Math.Min(A.z, B.z);
                maxZ = Math.Max(A.z, B.z);
                extent = 0.0;
            }
            else
            {
                minX = maxX = A.x;
                minZ = maxZ = A.z;
                extent = Math.Abs((double)Size);
            }

            double largestCoordinate = Math.Max(Math.Max(Math.Abs(minX), Math.Abs(maxX)), Math.Max(Math.Abs(minZ), Math.Abs(maxZ)));
            double margin = (extent + largestBodyRadius) * (1.0 + RelativeMargin) + AbsoluteMargin
                            + largestCoordinate * CoordinateMargin;
            double lowX = minX - margin, highX = maxX + margin, lowZ = minZ - margin, highZ = maxZ + margin;
            if (!double.IsFinite(lowX) || !double.IsFinite(highX) || !double.IsFinite(lowZ) || !double.IsFinite(highZ))
            {
                box = default;
                return false;
            }

            box = new HitBox(Down(lowX), Down(lowZ), Up(highX), Up(highZ));
            return true;
        }

        // A float at most (Down) or at least (Up) the double: rounded to the nearest, then a float further out.
        private static float Down(double value) => MathF.BitDecrement((float)value);

        private static float Up(double value) => MathF.BitIncrement((float)value);
    }

    /// <summary>A shape's box on X/Z, edges included (<see cref="Shape.TryBound" />).</summary>
    private readonly record struct HitBox(float MinX, float MinZ, float MaxX, float MaxZ);

    /// <summary>
    /// The grid's candidates for one query (#982). A cell's entry outside the box is left on the grid's copy of its X/Z,
    /// which is the entity's own, without the unit being read: no unit there can overlap the shape. Every other candidate,
    /// a cell's entry within the box, an entry in no cell or one not indexed, is tested on the unit as a full scan tests
    /// it.
    /// </summary>
    private struct HitCandidates(Shape shape, HitBox box, List<IUnit> hits, float largestBodyRadius) : ISpatialVisitor
    {
        public int Visited;
        public int Examined;

        public void VisitPlaced(SpatialKind kind, ulong rawGuid, float x, float z, IWorldObject obj)
        {
            Visited++;
            if (x < box.MinX || x > box.MaxX || z < box.MinZ || z > box.MaxZ)
            {
                AssertWithinLargest(obj);
                return;
            }

            Test(obj);
        }

        public void Visit(SpatialKind kind, ulong rawGuid, float x, float z, IWorldObject obj)
        {
            Visited++;
            Test(obj);
        }

        private void Test(IWorldObject obj)
        {
            Examined++;
            var unit = (IUnit)obj;
            if (IsAlive(unit) && shape.Overlaps(unit))
                hits.Add(unit);
        }

        /// <summary>
        /// Debug builds only: throws when a unit left out on its position has a body larger than the box allowed for, as
        /// one whose radius changed after it joined the grid would (<see cref="SpatialGrid.LargestBodyRadius" />).
        /// </summary>
        [Conditional("DEBUG")]
        private readonly void AssertWithinLargest(IWorldObject obj)
        {
            float radius = ((IUnit)obj).BodyRadius;
            if (radius > largestBodyRadius)
            {
                throw new InvalidOperationException(
                    $"{obj.Guid} has a body radius of {radius} m, above the {largestBodyRadius} m its spatial grid " +
                    "widens a hit query's box by: a body radius changed after its unit joined the grid (#982).");
            }
        }
    }

    /// <summary>
    /// Nearest the origin first, on X/Z; ties broken by raw guid, so the order never depends on the dictionaries' or the
    /// grid's. Its <see cref="Comparison" /> is made once: a sort by an <c>IComparer</c> makes a delegate of its
    /// <c>Compare</c> on every call.
    /// </summary>
    private sealed class NearestFirst
    {
        public NearestFirst() => Comparison = Compare;

        public Vector3 Origin { get; set; }

        public Comparison<IUnit> Comparison { get; }

        private int Compare(IUnit a, IUnit b)
        {
            int byDistance = HitShapes.Distance2D(Origin, a.Position).CompareTo(HitShapes.Distance2D(Origin, b.Position));
            return byDistance != 0 ? byDistance : a.Guid.RawValue.CompareTo(b.Guid.RawValue);
        }
    }
}

/// <summary>What a <see cref="UnitHitQuery" />'s queries of one shape did (#982), for measuring the grid alone.</summary>
internal struct HitQueryCounts
{
    /// <summary>Queries asked.</summary>
    public long Queries;

    /// <summary>The instance's characters and creatures at each query, summed: what a full scan reads.</summary>
    public long Members;

    /// <summary>The grid's entries handed to the query (a full scan's members when it scanned).</summary>
    public long Visited;

    /// <summary>The units read and tested (a full scan's members when it scanned).</summary>
    public long Examined;

    /// <summary>The units hit.</summary>
    public long Hits;
}
