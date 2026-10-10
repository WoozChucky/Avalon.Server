using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Units;

namespace Avalon.World.Abilities.Targeting;

/// <summary>
/// <see cref="IHitQuery" /> over an instance's live dictionaries. Tick thread only, like the
/// dictionaries it reads.
/// </summary>
/// <remarks>
/// A query allocates nothing once its lists exist (#880): the dictionaries are walked with their struct enumerators,
/// the shape is tested and the hits sorted without a new delegate or closure, and each result is a list the query keeps
/// and reuses. A list handed out is not reused before <see cref="Recycle" />, which the instance calls at the start of
/// each tick: every result stays valid for the rest of the tick it was asked in, however many queries follow, a query
/// asked while another's result is being walked (a hit that leads to another cast) included. A query nobody recycles
/// (a test's) keeps every list it handed out.
/// </remarks>
public sealed class UnitHitQuery(
    Dictionary<ObjectGuid, ICharacter> characters,
    Dictionary<ObjectGuid, ICreature> creatures) : IHitQuery
{
    private readonly List<List<IUnit>> _lists = [];
    private readonly NearestFirst _nearestFirst = new();
    private int _handedOut;

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
    /// Every list handed out may be reused from now on. The instance calls it at the start of its tick, when no result
    /// of the last one is still being read.
    /// </summary>
    public void Recycle() => _handedOut = 0;

    private List<IUnit> Collect(Vector3 origin, in Shape shape)
    {
        List<IUnit> hits = NextList();

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

        _nearestFirst.Origin = origin;
        hits.Sort(_nearestFirst.Comparison);

        return hits;
    }

    /// <summary>A list not handed out since the last <see cref="Recycle" />, emptied; a new one when every one is.</summary>
    private List<IUnit> NextList()
    {
        if (_handedOut == _lists.Count)
            _lists.Add([]);

        List<IUnit> list = _lists[_handedOut++];
        list.Clear();
        return list;
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
        public bool Overlaps(IUnit unit) => Kind switch
        {
            ShapeKind.Circle => HitShapes.CircleOverlaps(A, Size, unit.Position, unit.BodyRadius),
            ShapeKind.Cone => HitShapes.ConeOverlaps(A, B, Size, Arc, unit.Position, unit.BodyRadius),
            _ => HitShapes.SegmentOverlaps(A, B, unit.Position, unit.BodyRadius),
        };
    }

    /// <summary>
    /// Nearest the origin first, on X/Z; ties broken by raw guid, so the order never depends on the dictionaries'. Its
    /// <see cref="Comparison" /> is made once: a sort by an <c>IComparer</c> makes a delegate of its <c>Compare</c> on
    /// every call.
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
