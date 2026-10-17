using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Abilities.Targeting;
using Avalon.World.ChunkLayouts;
using Avalon.World.Creatures;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Units;

namespace Avalon.World.Instances;

/// <summary>What an entry of a <see cref="SpatialGrid" /> is: which of its instance's dictionaries holds it.</summary>
internal enum SpatialKind : byte
{
    Creature,
    Character,
}

/// <summary>Where an entity is indexed (#982): the grid of the instance it is in, and its slot there; empty outside one.</summary>
internal struct SpatialHandle
{
    public SpatialGrid? Grid;
    public int Slot;
}

/// <summary>What a <see cref="SpatialGrid" /> walk is handed, one entry at a time.</summary>
internal interface ISpatialVisitor
{
    /// <summary>
    /// An entry of a cell: its kind, its guid's raw value, its X/Z as of its last position write, and the entity itself,
    /// which the visitor need not read when the values decide. Every component of that position (X, Y and Z) is finite,
    /// and the values are the entity's own, since every position write reaches the grid through the concrete setters (a
    /// Debug build checks each entry as the walk reaches it).
    /// </summary>
    void VisitPlaced(SpatialKind kind, ulong rawGuid, float x, float z, IWorldObject obj);

    /// <summary>
    /// An entry in no cell (a component of its position is not finite) or not indexed (its X/Z read live), for the
    /// visitor to decide on the entity.
    /// </summary>
    void Visit(SpatialKind kind, ulong rawGuid, float x, float z, IWorldObject obj);
}

/// <summary>
/// An instance's characters and creatures by where they stand (#982): a uniform grid on X/Z over the layout's chunks,
/// one cell per chunk cell (30 m), so a query visits the entities of the cells around a point instead of every entity of
/// the instance. One per <see cref="MapInstance" />, filled as its members join (creatures off the tick, as the instance
/// is built); tick thread only, like the instance's dictionaries (#639). World-side: nothing of it is in World.Public.
/// </summary>
/// <remarks>
/// <para>
/// <b>Entries.</b> Each indexed entity has a slot in one array: its X and Z, its guid's raw value, the entity, its kind
/// and its cell, so a query rejects a far entry without loading the entity. Each cell is an intrusive doubly-linked list
/// through the slots, so a move to another cell is an unlink and a link, and nothing is allocated after a join: the array
/// grows only when an entity joins and finds no free slot.
/// </para>
/// <para>
/// <b>Kept current</b> by the two concrete position setters (<c>Creature.Position</c>, <c>CharacterEntity.Position</c>),
/// which every write reaches, through <c>IWorldObject.Position</c> too: an entity holds its <see cref="SpatialHandle" />
/// while a member, and its setter hands the grid the new position (<see cref="Moved" />), relinked only when its cell
/// changed. The handle is cleared when it leaves, so a position written outside an instance touches no grid.
/// </para>
/// <para>
/// <b>Cells.</b> Coordinates are clamped to the edge cells, so an entity off the layout is in the nearest edge cell and a
/// query from off the layout still finds what is near it. A position with any non-finite component (X, Y or Z) is in no
/// cell but in a list of its own every query visits: no rule finds such an entity in range, but the watcher's own
/// character is in its own view wherever it stands. An entity of another type (a test's double) is not indexed: it is
/// kept in a list every query visits, its position read live; so is one indexed by another instance's grid as well (a
/// character added to an instance while still in another), which that other grid indexes from then on.
/// </para>
/// <para>
/// <b>Creature searches.</b> A creature whose range search comes due asks <see cref="MayHaveCharacterWithin" />, and the
/// instance's creature scheduler skips the search when the answer is no (#982). The grid keeps its characters' X/Z bounds,
/// bound again lazily after any character linked, unlinked or moved, which answer most of those with a few comparisons;
/// past them, each cell's count of characters lets the probe walk only the cells around the creature that hold one.
/// </para>
/// <para>
/// <b>Moves for the position histories (#1082).</b> The same setter call lists the entity among the entries moved since
/// the instance last recorded the histories (<see cref="RecordMoved" />, its Step 4b), once however often it moved, so
/// that step reads the units that moved and no other (most of a forest's creatures are waiting, #979). Each move also
/// measures how far the entity is, on X and on Z, from where its history was last recorded; the largest of each tick is
/// kept by tick (<see cref="RewindDrift" />), which bounds how far a rewound hit query's units can be from where the grid
/// has them.
/// </para>
/// </remarks>
internal sealed class SpatialGrid
{
    private const int InitialCapacity = 16;
    private const int InitialCharacterCapacity = 8;
    private const int Unlinked = -1;

    // A layout far larger than any generated or authored map gets coarser cells rather than an array of this many.
    private const int MaxCells = 1 << 16;

    private readonly float _originX;
    private readonly float _originZ;
    private readonly float _cellSize;
    private readonly float _inverseCellSize;
    private readonly int _columns;
    private readonly int _rows;

    // Each cell's first slot, then, at _unplaced, the first slot of the entries in no cell (a non-finite position).
    private readonly int[] _heads;
    private readonly int _unplaced;

    // How many characters each cell's list holds, the entries in no cell at _unplaced: a creature's search probe skips a
    // cell without one without walking its creatures (#982).
    private readonly int[] _characterCounts;

    // The characters' slots, packed, each entry holding its place here: the box below is bound over them alone.
    private int[] _characterSlots = new int[InitialCharacterCapacity];
    private int _characterSlotCount;

    // The X/Z bounds of the characters in a cell, as of _boundVersion. _characterVersion moves on every link, unlink and
    // move of a character, so the box is bound again, lazily, only when a creature's search asks after a change (#982).
    private long _characterVersion;
    private long _boundVersion = -1;
    private float _boundMinX, _boundMaxX, _boundMinZ, _boundMaxZ;

    private Entry[] _entries = new Entry[InitialCapacity];
    private int _used;
    private int _free = Unlinked;

    // Members that are not indexed (another type, or indexed by another instance's grid), visited by every query.
    private readonly List<IWorldObject> _unindexedCreatures = [];
    private readonly List<IWorldObject> _unindexedCharacters = [];

    // The largest body radius of any entity this grid has indexed (#982), for the hit query's box: 0 until one joins.
    private float _largestBodyRadius;

    // The slots moved since the last RecordMoved, packed, each entry holding its place here plus one (#1082). As long as
    // the entries, grown with them on a join only, and never holding a slot twice, so a move never grows it.
    private int[] _moved = new int[InitialCapacity];
    private int _movedCount;

    // The largest step (on X or on Z, from an entity's recorded X/Z) of any move since the last RecordMoved (#1082).
    private float _pendingStep;

    // The largest step of each recorded tick, by tick (#1082): the rewind cap's ticks and the history's margin.
    private readonly (long Tick, float Step)[] _steps = new (long Tick, float Step)[PositionHistory.Capacity];

    private SpatialGrid(float originX, float originZ, float cellSize, int columns, int rows)
    {
        _originX = originX;
        _originZ = originZ;
        _cellSize = cellSize;
        _inverseCellSize = 1f / cellSize;
        _columns = columns;
        _rows = rows;
        _unplaced = columns * rows;
        _heads = new int[_unplaced + 1];
        Array.Fill(_heads, Unlinked);
        _characterCounts = new int[_unplaced + 1];
        for (int i = 0; i < _steps.Length; i++)
            _steps[i].Tick = long.MinValue;
    }

    /// <summary>
    /// The grid over <paramref name="layout" />'s chunks: from the lowest chunk corner to the far side of the highest, one
    /// cell per chunk cell (<see cref="ChunkLayout.CellSize" />). A layout with no chunk or no usable cell size gets one
    /// cell, which every query visits whole.
    /// </summary>
    public static SpatialGrid For(ChunkLayout layout)
    {
        float cellSize = layout.CellSize;
        if (layout.Chunks.Count == 0 || !float.IsFinite(cellSize) || cellSize <= 0f)
            return new SpatialGrid(0f, 0f, 1f, 1, 1);

        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        foreach (PlacedChunk chunk in layout.Chunks)
        {
            minX = MathF.Min(minX, chunk.WorldPos.x);
            minZ = MathF.Min(minZ, chunk.WorldPos.z);
            maxX = MathF.Max(maxX, chunk.WorldPos.x);
            maxZ = MathF.Max(maxZ, chunk.WorldPos.z);
        }

        if (!float.IsFinite(minX) || !float.IsFinite(minZ) || !float.IsFinite(maxX) || !float.IsFinite(maxZ))
            return new SpatialGrid(0f, 0f, 1f, 1, 1);

        double width = (double)maxX - minX + cellSize;
        double depth = (double)maxZ - minZ + cellSize;
        while (Math.Ceiling(width / cellSize) * Math.Ceiling(depth / cellSize) > MaxCells)
            cellSize *= 2f;

        int columns = Math.Max(1, (int)Math.Ceiling(width / cellSize));
        int rows = Math.Max(1, (int)Math.Ceiling(depth / cellSize));
        return new SpatialGrid(minX, minZ, cellSize, columns, rows);
    }

    /// <summary>
    /// Adds a member of the instance, at its position now. One already here is left as it is; one indexed by another
    /// instance's grid is indexed by this one from now on (that grid keeps it, unindexed, until it is removed there).
    /// May grow the entries: an instance's creatures join as it is built, off the tick.
    /// </summary>
    public void Add(IWorldObject obj, SpatialKind kind)
    {
        ref SpatialHandle handle = ref HandleOf(obj);
        if (Unsafe.IsNullRef(ref handle))
        {
            List<IWorldObject> unindexed = Unindexed(kind);
            if (!Contains(unindexed, obj))
                unindexed.Add(obj);
            return;
        }

        if (ReferenceEquals(handle.Grid, this))
            return;

        // Kept by this grid unindexed while another indexed it: indexed here from now on.
        RemoveReference(Unindexed(kind), obj);
        handle.Grid?.Unindex(handle.Slot);

        int slot = Allocate();
        Vector3 position = obj.Position;
        ref Entry entry = ref _entries[slot];
        entry.X = position.x;
        entry.Z = position.z;
        entry.RawGuid = obj.Guid.RawValue;
        entry.Object = obj;
        entry.Kind = kind;
        entry.RecordedX = position.x;   // where the instance seeds its history as it joins (#1082)
        entry.RecordedZ = position.z;
        Link(slot, CellOf(position));
        if (kind == SpatialKind.Character)
        {
            if (_characterSlotCount == _characterSlots.Length)
                Array.Resize(ref _characterSlots, _characterSlots.Length * 2); // Grows on a join only, as the entries do.
            entry.CharacterIndex = _characterSlotCount;
            _characterSlots[_characterSlotCount++] = slot;
        }

        handle.Grid = this;
        handle.Slot = slot;

        // A NaN radius overlaps nothing, so it is left out; an infinite one makes every hit query scan in full.
        if (obj is IUnit unit && unit.BodyRadius > _largestBodyRadius)
            _largestBodyRadius = unit.BodyRadius;
    }

    /// <summary>
    /// The largest body radius (<see cref="IUnit.BodyRadius" />) of any entity this grid has indexed since it was built,
    /// those that left included, so never below that of an entity in a cell (#982): a hit query widens its box by it. An
    /// entity's body radius is fixed for its life (a creature's is copied from its template when it is spawned, a
    /// character's is a constant), so it is taken as the entity joins; a creature spawned after a template's radius changed
    /// joins with the new one. An entity not indexed is not counted: every query visits those whatever their position.
    /// </summary>
    public float LargestBodyRadius => _largestBodyRadius;

    /// <summary>Removes a member that left the instance, and clears its handle. Nothing happens for one not here.</summary>
    public void Remove(IWorldObject obj)
    {
        ref SpatialHandle handle = ref HandleOf(obj);
        if (!Unsafe.IsNullRef(ref handle) && ReferenceEquals(handle.Grid, this))
        {
            Release(handle.Slot);
            handle = default;
            return;
        }

        if (!RemoveReference(_unindexedCreatures, obj))
            RemoveReference(_unindexedCharacters, obj);
    }

    /// <summary>
    /// The entity in <paramref name="slot" /> was moved to <paramref name="position" />: its X and Z are written, and it
    /// is relinked only when its cell changed. Called by the concrete position setters, on every write. It is listed for
    /// the next <see cref="RecordMoved" />, and its step from where its history was last recorded counts towards this
    /// tick's drift, unless it jumped (<see cref="Jumped" />) since: then its steps are measured from where it landed
    /// (#1082).
    /// </summary>
    public void Moved(int slot, Vector3 position)
    {
        ref Entry entry = ref _entries[slot];
        entry.X = position.x;
        entry.Z = position.z;
        if (entry.Kind == SpatialKind.Character)
            _characterVersion++;

        int cell = CellOf(position);
        if (cell != entry.Cell)
        {
            Unlink(slot);
            Link(slot, cell);
        }

        if (entry.Jumped)
        {
            entry.RecordedX = position.x;
            entry.RecordedZ = position.z;
            entry.Jumped = false;
        }
        else
        {
            // MathF.Max keeps a NaN, as the sums do, so a step that is not finite makes a rewound query scan every member.
            float step = MathF.Max(MathF.Abs(position.x - entry.RecordedX), MathF.Abs(position.z - entry.RecordedZ));
            _pendingStep = MathF.Max(_pendingStep, step);
        }

        ListMoved(ref entry, slot);
    }

    /// <summary>
    /// The entity in <paramref name="slot" /> is about to jump (#1082): a teleport or a revive, whose history has just
    /// been emptied. It is listed for the next <see cref="RecordMoved" />, which seeds the history where it stands then,
    /// even where it stood before, and its next move is measured from where it lands rather than counted as drift: a
    /// rewound query finds no position of it from before the jump, so it tests its present one.
    /// </summary>
    public void Jumped(int slot)
    {
        ref Entry entry = ref _entries[slot];
        entry.Jumped = true;
        ListMoved(ref entry, slot);
    }

    /// <summary>
    /// Step 4b of the instance's tick (#1082): records every entity moved (or jumped) since the last call into its
    /// position history at <paramref name="tick" /> (<see cref="PositionHistory.Record" />), and keeps the largest step of
    /// those moves as <paramref name="tick" />'s. Returns how many it recorded. Reads no entity that did not move, and
    /// allocates nothing.
    /// </summary>
    /// <remarks>
    /// A tick recorded twice (a world tick that threw is not counted, so the next one carries its number) adds its second
    /// largest step to the first: the history's entry for the tick is the later position, which may be both steps away
    /// from the one before.
    /// </remarks>
    public int RecordMoved(long tick)
    {
        Entry[] entries = _entries;
        int count = _movedCount;
        for (int i = 0; i < count; i++)
        {
            ref Entry entry = ref entries[_moved[i]];
            entry.MovedIndex = 0;
            entry.Jumped = false;
            entry.RecordedX = entry.X;
            entry.RecordedZ = entry.Z;

            IWorldObject obj = entry.Object!;
            PositionHistory? history = obj switch
            {
                Creature creature => creature.History,
                CharacterEntity character => character.History,
                _ => null,
            };
            history?.Record(tick, obj.Position);
        }

        _movedCount = 0;

        ref (long Tick, float Step) recorded = ref _steps[StepIndex(tick)];
        if (recorded.Tick == tick)
        {
            recorded.Step += _pendingStep;
        }
        else
        {
            recorded.Tick = tick;
            recorded.Step = _pendingStep;
        }

        _pendingStep = 0f;
        return count;
    }

    /// <summary>
    /// How far, on X or on Z, any entity in a cell can stand now from where its position history puts it at
    /// <paramref name="tick" /> (#1082): the largest steps recorded on each tick after it up to <paramref name="now" /> (a
    /// tick with none recorded adds 0), plus the largest of the moves since the last record. An entity's displacement over
    /// those ticks is at most the sum of its steps, each at most its tick's largest; a jumped entity has no position from
    /// before its jump, so its rewound position is its present one. Infinite for a window longer than the ticks kept, and
    /// NaN after a step that was not finite: either way a hit query scans every member.
    /// </summary>
    public float RewindDrift(long tick, long now)
    {
        if (now - tick > _steps.Length)
            return float.PositiveInfinity;

        float drift = _pendingStep;
        for (long t = tick + 1; t <= now; t++)
        {
            ref readonly (long Tick, float Step) recorded = ref _steps[StepIndex(t)];
            if (recorded.Tick == t)
                drift += recorded.Step;
        }

        return drift;
    }

    // A tick's place in _steps; world ticks are never negative.
    private int StepIndex(long tick) => (int)(tick % _steps.Length);

    // Lists an entry for the next RecordMoved, once (#1082).
    private void ListMoved(ref Entry entry, int slot)
    {
        if (entry.MovedIndex != 0)
            return;

        _moved[_movedCount++] = slot;
        entry.MovedIndex = _movedCount;
    }

    /// <summary>
    /// Hands <paramref name="visitor" /> every entity that can be within <paramref name="reach" /> of (x, z) on X/Z: the
    /// entities of every cell the circle overlaps (widened a little, so a float rounded either way is never missed), the
    /// entities in no cell, and the entities not indexed, each once. It is the visitor's to decide which are in range. A
    /// reach that is not finite visits every cell, and a centre that is not finite none (no rule finds anything in range
    /// of it), apart from the two lists.
    /// </summary>
    public void Visit<TVisitor>(float x, float z, float reach, ref TVisitor visitor)
        where TVisitor : struct, ISpatialVisitor
    {
        if (float.IsFinite(x) && float.IsFinite(z))
        {
            // The cells are found with the reach widened by a part in 1,024 and 0.25 m, far more than the rounding of a
            // distance or a cell index can be at any map's coordinates; the visitor's own test is the exact one.
            float widened = MathF.Abs(reach) * (1f + 1f / 1024f) + 0.25f;
            if (float.IsFinite(widened))
                VisitCircle(x, z, widened, ref visitor);
            else
                VisitAllCells(ref visitor);
        }

        VisitUnplaced(ref visitor);
        VisitUnindexed(_unindexedCreatures, SpatialKind.Creature, ref visitor);
        VisitUnindexed(_unindexedCharacters, SpatialKind.Character, ref visitor);
    }

    /// <summary>
    /// Hands <paramref name="visitor" /> every entity whose X/Z can be within the box from (<paramref name="minX" />,
    /// <paramref name="minZ" />) to (<paramref name="maxX" />, <paramref name="maxZ" />), edges included: the entities of
    /// every cell the box overlaps, the entities in no cell, and the entities not indexed, each once (#982). It is the
    /// visitor's to decide which are in the box: a cell's entries outside it are handed over too. An entry whose X is
    /// within [<paramref name="minX" />, <paramref name="maxX" />] is in a column from the one of <paramref name="minX" />
    /// to the one of <paramref name="maxX" />, as a cell is found from the same floats by a rule monotonic in each
    /// coordinate, and so on Z. Bounds beyond the layout are clamped to its edge cells, infinite ones too; a bound that
    /// is NaN visits every cell.
    /// </summary>
    public void VisitBox<TVisitor>(float minX, float minZ, float maxX, float maxZ, ref TVisitor visitor)
        where TVisitor : struct, ISpatialVisitor
    {
        if (float.IsNaN(minX) || float.IsNaN(minZ) || float.IsNaN(maxX) || float.IsNaN(maxZ))
        {
            VisitAllCells(ref visitor);
        }
        else
        {
            int firstColumn = Column(minX);
            int lastColumn = Column(maxX);
            int lastRow = Row(maxZ);
            for (int row = Row(minZ); row <= lastRow; row++)
            {
                int rowStart = row * _columns;
                for (int column = firstColumn; column <= lastColumn; column++)
                    VisitCell(_heads[rowStart + column], ref visitor);
            }
        }

        VisitUnplaced(ref visitor);
        VisitUnindexed(_unindexedCreatures, SpatialKind.Creature, ref visitor);
        VisitUnindexed(_unindexedCharacters, SpatialKind.Character, ref visitor);
    }

    private void VisitCircle<TVisitor>(float x, float z, float reach, ref TVisitor visitor)
        where TVisitor : struct, ISpatialVisitor
    {
        int lastRow = Row(z + reach);
        for (int row = Row(z - reach); row <= lastRow; row++)
        {
            if (!RowSpan(row, x, z, reach, out int first, out int last))
                continue;

            for (int cell = first; cell <= last; cell++)
                VisitCell(_heads[cell], ref visitor);
        }
    }

    /// <summary>
    /// The cells of <paramref name="row" /> that a circle of <paramref name="reach" /> around (x, z) overlaps, from
    /// <paramref name="first" /> to <paramref name="last" /> (cell indexes); false when it overlaps none of them. The one
    /// walk every circle query takes: views (<see cref="Visit{TVisitor}" />) and a creature's search probe
    /// (<see cref="MayHaveCharacterWithin" />).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool RowSpan(int row, float x, float z, float reach, out int first, out int last)
    {
        // How far the row's band is from the centre on Z; the edge rows reach on outwards, as their cells are clamped.
        float bandLow = row == 0 ? float.NegativeInfinity : _originZ + row * _cellSize;
        float bandHigh = row == _rows - 1 ? float.PositiveInfinity : _originZ + (row + 1) * _cellSize;
        float dz = z < bandLow ? bandLow - z : z > bandHigh ? z - bandHigh : 0f;
        if (dz > reach)
        {
            first = 0;
            last = -1;
            return false;
        }

        float halfWidth = MathF.Sqrt(reach * reach - dz * dz);
        int rowStart = row * _columns;
        first = rowStart + Column(x - halfWidth);
        last = rowStart + Column(x + halfWidth);
        return true;
    }

    /// <summary>
    /// Whether a creature's range detector at <paramref name="centre" /> could find a character within
    /// <paramref name="range" /> (#982): false only when its search would find none, so a due search may be skipped. It
    /// may answer true for a search that then finds nothing; never false for one that would find someone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The search (<c>CreatureRangeDetectorScript</c>) takes a character whose 3D <c>Vector3.Distance</c> from the creature
    /// is not above its range, in line of sight. That distance is computed as the float differences, their squares summed
    /// in double in X, Y, Z order, the square root rounded to float. This measures X/Z alone the same way: the same float
    /// differences and squares, the Y square left out. Each step rounds monotonically and the Y square is never negative, so
    /// with every component finite the 3D distance is never below this one, overflow to infinity included: a character
    /// this finds beyond the range is beyond it for the search too. Its cells are found with the reach widened as a view's
    /// are (<see cref="Visit{TVisitor}" />), so every character within the range on X/Z is in one of them.
    /// </para>
    /// <para>
    /// <b>The box first.</b> Before any cell, the centre is tested against the X/Z bounds of the characters in a cell,
    /// widened by the range: a few float comparisons, no square root and no loop, which answer most searches. The test is
    /// exact in floats, with no margin needed. For a character at (x, z) inside the bounds, x ≥ minX, so x − cx ≥ minX − cx,
    /// and float subtraction rounds monotonically: fl(x − cx) ≥ fl(minX − cx). When fl(minX − cx) &gt; range, the
    /// character's |fl(cx − x)| = |fl(x − cx)| is above the range too (overflow to infinity included), and the distance
    /// above, whose double sum is never below the float square of that difference (its square root is the difference
    /// itself, exactly), is above the range: the search would not take it. The same holds on each side and on Z. The
    /// bounds are those of every character in a cell: bound again from the characters' entries whenever
    /// <c>_characterVersion</c> has moved since, which every link, unlink and move of a character does, so a character
    /// moved earlier in the step is where the box says. With no character in a cell the bounds are empty (+∞ to −∞) and
    /// every centre is outside them.
    /// </para>
    /// <para>
    /// Everything else is answered true, before the box, as the search's own arithmetic could find it: a centre or range
    /// not finite (a NaN distance is not above any range, nor is any distance above an infinite one), any character in no
    /// cell (a component of its position not finite) and any character not indexed (its position read live). A dead
    /// character counts as any other (the search passes it over; counting it only lets a search run). The dictionary order
    /// in which the search takes the first match is the search's: this decides only whether it runs.
    /// </para>
    /// </remarks>
    public bool MayHaveCharacterWithin(Vector3 centre, float range)
    {
        if (SearchWork.Counted)
            ProbesRun++;
        if (_characterCounts[_unplaced] > 0 || _unindexedCharacters.Count > 0)
            return true;

        if (!float.IsFinite(centre.x) || !float.IsFinite(centre.y) || !float.IsFinite(centre.z) || !float.IsFinite(range))
            return true;

        if (_boundVersion != _characterVersion)
            BoundCharacters();
        AssertBound();

        if (_boundMinX - centre.x > range || centre.x - _boundMaxX > range
            || _boundMinZ - centre.z > range || centre.z - _boundMaxZ > range)
        {
            if (SearchWork.Counted)
                BoundSkips++;
            return false;
        }

        if (ProbeCells(centre, range))
            return true;

        if (SearchWork.Counted)
            CellSkips++;
        return false;
    }

    /// <summary>The characters' X/Z bounds again, from the entries of those in a cell; those in no cell answer before.</summary>
    private void BoundCharacters()
    {
        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
        Entry[] entries = _entries;
        for (int i = 0; i < _characterSlotCount; i++)
        {
            ref Entry entry = ref entries[_characterSlots[i]];
            if (entry.Cell == _unplaced)
                continue;

            minX = MathF.Min(minX, entry.X);
            maxX = MathF.Max(maxX, entry.X);
            minZ = MathF.Min(minZ, entry.Z);
            maxZ = MathF.Max(maxZ, entry.Z);
        }

        _boundMinX = minX;
        _boundMaxX = maxX;
        _boundMinZ = minZ;
        _boundMaxZ = maxZ;
        _boundVersion = _characterVersion;
        if (SearchWork.Counted)
            Bounds++;
    }

    /// <summary>
    /// Debug builds only: throws when a character in a cell lies outside the bounds the box was last bound to, as a
    /// character moved without its version moving would (a position written past the setter, or a version not kept).
    /// </summary>
    [Conditional("DEBUG")]
    private void AssertBound()
    {
        for (int i = 0; i < _characterSlotCount; i++)
        {
            ref Entry entry = ref _entries[_characterSlots[i]];
            AssertCurrent(ref entry);
            if (entry.Cell != _unplaced && !(entry.X >= _boundMinX && entry.X <= _boundMaxX
                                             && entry.Z >= _boundMinZ && entry.Z <= _boundMaxZ))
            {
                throw new InvalidOperationException(
                    $"The spatial grid's character bounds ({_boundMinX}, {_boundMinZ}) to ({_boundMaxX}, {_boundMaxZ}) leave out " +
                    $"{new ObjectGuid(entry.RawGuid)} at ({entry.X}, {entry.Z}): a character moved without its version moving (#982).");
            }
        }
    }

    /// <summary>Whether a character in the cells around <paramref name="centre" /> is within <paramref name="range" /> on X/Z.</summary>
    private bool ProbeCells(Vector3 centre, float range)
    {
        float reach = MathF.Abs(range) * (1f + 1f / 1024f) + 0.25f;
        Entry[] entries = _entries;
        int lastRow = Row(centre.z + reach);
        for (int row = Row(centre.z - reach); row <= lastRow; row++)
        {
            if (!RowSpan(row, centre.x, centre.z, reach, out int first, out int last))
                continue;

            for (int cell = first; cell <= last; cell++)
            {
                if (SearchWork.Counted)
                    ProbedCells++;
                if (_characterCounts[cell] == 0)
                    continue;

                for (int slot = _heads[cell]; slot != Unlinked; slot = entries[slot].Next)
                {
                    ref Entry entry = ref entries[slot];
                    AssertCurrent(ref entry);
                    if (SearchWork.Counted)
                        ProbedEntries++;
                    if (entry.Kind == SpatialKind.Character && WithinOnXZ(centre, entry.X, entry.Z, range))
                        return true;
                }
            }
        }

        return false;
    }

    // Vector3.Distance(centre, (x, y, z)) without the Y term: the same float differences, double squares and sum order.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool WithinOnXZ(Vector3 centre, float x, float z, float range)
    {
        float dx = centre.x - x;
        float dz = centre.z - z;
        return (float)Math.Sqrt((double)dx * (double)dx + (double)dz * (double)dz) <= range;
    }

    /// <summary>
    /// Work counts of <see cref="MayHaveCharacterWithin" />: probes run, the bounds bound again, the probes the box answered
    /// no and those the cells did, cells looked at, entries walked (in cells with a character),
    /// counted only when <see cref="SearchWork.Counted" />, for tests and the <c>detection-counts</c> harness.
    /// </summary>
    internal long ProbesRun { get; private set; }

    /// <inheritdoc cref="ProbesRun" />
    internal long Bounds { get; private set; }

    /// <inheritdoc cref="ProbesRun" />
    internal long BoundSkips { get; private set; }

    /// <inheritdoc cref="ProbesRun" />
    internal long CellSkips { get; private set; }

    /// <inheritdoc cref="ProbesRun" />
    internal long ProbedCells { get; private set; }

    /// <inheritdoc cref="ProbesRun" />
    internal long ProbedEntries { get; private set; }

    private void VisitAllCells<TVisitor>(ref TVisitor visitor) where TVisitor : struct, ISpatialVisitor
    {
        for (int cell = 0; cell < _unplaced; cell++)
            VisitCell(_heads[cell], ref visitor);
    }

    private void VisitCell<TVisitor>(int slot, ref TVisitor visitor) where TVisitor : struct, ISpatialVisitor
    {
        Entry[] entries = _entries;
        while (slot != Unlinked)
        {
            ref Entry entry = ref entries[slot];
            AssertCurrent(ref entry);
            visitor.VisitPlaced(entry.Kind, entry.RawGuid, entry.X, entry.Z, entry.Object!);
            slot = entry.Next;
        }
    }

    private void VisitUnplaced<TVisitor>(ref TVisitor visitor) where TVisitor : struct, ISpatialVisitor
    {
        Entry[] entries = _entries;
        int slot = _heads[_unplaced];
        while (slot != Unlinked)
        {
            ref Entry entry = ref entries[slot];
            AssertCurrent(ref entry);
            visitor.Visit(entry.Kind, entry.RawGuid, entry.X, entry.Z, entry.Object!);
            slot = entry.Next;
        }
    }

    /// <summary>
    /// Debug builds only, so nothing in Release: throws when an entry no longer matches its entity, its X/Z, its cell
    /// (which also says whether every component of the position is finite) or its guid. A position written past the
    /// concrete setter, to a character's row say (<c>Data.X = ...</c>) while it is in an instance, leaves the grid
    /// behind, and a view decided on the entry's values would be decided wrong; this makes every test that decides a view
    /// fail instead. It throws rather than calling <c>Debug.Assert</c>, which would end a test host instead of failing a
    /// test (as <c>TickThreadGuard</c> does).
    /// </summary>
    [Conditional("DEBUG")]
    private void AssertCurrent(ref Entry entry)
    {
        IWorldObject obj = entry.Object!;
        Vector3 position = obj.Position;
        if (entry.X.Equals(position.x) && entry.Z.Equals(position.z) && entry.Cell == CellOf(position)
            && entry.RawGuid == obj.Guid.RawValue)
        {
            return;
        }

        throw new InvalidOperationException(
            $"The spatial grid holds {new ObjectGuid(entry.RawGuid)} at ({entry.X}, {entry.Z}) in cell {entry.Cell}, but " +
            $"it is {obj.Guid} at {position}: a position written past its Position setter while it is in an instance " +
            "leaves the grid behind (#982).");
    }

    private static void VisitUnindexed<TVisitor>(List<IWorldObject> unindexed, SpatialKind kind, ref TVisitor visitor)
        where TVisitor : struct, ISpatialVisitor
    {
        for (int i = 0; i < unindexed.Count; i++)
        {
            IWorldObject obj = unindexed[i];
            Vector3 position = obj.Position;
            visitor.Visit(kind, obj.Guid.RawValue, position.x, position.z, obj);
        }
    }

    // The cell of a position, its coordinates clamped to the edge cells; _unplaced when a component is not finite.
    // Monotonic in X and in Z, which is what lets a query find a cell range from the ends of its circle.
    private int CellOf(Vector3 position)
    {
        if (!float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z))
            return _unplaced;

        return Row(position.z) * _columns + Column(position.x);
    }

    private int Column(float x)
    {
        float cell = (x - _originX) * _inverseCellSize;
        return cell <= 0f ? 0 : cell >= _columns - 1 ? _columns - 1 : (int)cell;
    }

    private int Row(float z)
    {
        float cell = (z - _originZ) * _inverseCellSize;
        return cell <= 0f ? 0 : cell >= _rows - 1 ? _rows - 1 : (int)cell;
    }

    private void Link(int slot, int cell)
    {
        ref Entry entry = ref _entries[slot];
        int head = _heads[cell];
        entry.Cell = cell;
        entry.Prev = Unlinked;
        entry.Next = head;
        if (head != Unlinked)
            _entries[head].Prev = slot;
        _heads[cell] = slot;
        if (entry.Kind == SpatialKind.Character)
        {
            _characterCounts[cell]++;
            _characterVersion++;
        }
    }

    private void Unlink(int slot)
    {
        ref Entry entry = ref _entries[slot];
        if (entry.Kind == SpatialKind.Character)
        {
            _characterCounts[entry.Cell]--;
            _characterVersion++;
        }

        if (entry.Prev != Unlinked)
            _entries[entry.Prev].Next = entry.Next;
        else
            _heads[entry.Cell] = entry.Next;

        if (entry.Next != Unlinked)
            _entries[entry.Next].Prev = entry.Prev;
    }

    private int Allocate()
    {
        if (_free != Unlinked)
        {
            int reused = _free;
            _free = _entries[reused].Next;
            return reused;
        }

        if (_used == _entries.Length)
        {
            Array.Resize(ref _entries, _entries.Length * 2);
            Array.Resize(ref _moved, _entries.Length);   // on a join only, as the entries grow
        }

        return _used++;
    }

    private void Release(int slot)
    {
        Unlink(slot);
        if (_entries[slot].MovedIndex != 0)
        {
            // The last moved slot takes this one's place in the moved list (#1082).
            int index = _entries[slot].MovedIndex - 1;
            int last = _moved[--_movedCount];
            _moved[index] = last;
            _entries[last].MovedIndex = index + 1;
        }

        if (_entries[slot].Kind == SpatialKind.Character)
        {
            // The last character's slot takes this one's place in the packed list.
            int index = _entries[slot].CharacterIndex;
            int moved = _characterSlots[--_characterSlotCount];
            _characterSlots[index] = moved;
            _entries[moved].CharacterIndex = index;
        }

        _entries[slot] = default;
        _entries[slot].Next = _free;
        _free = slot;
    }

    // The entity in this slot is indexed by another instance's grid now: kept here, unindexed, until it leaves here.
    private void Unindex(int slot)
    {
        ref Entry entry = ref _entries[slot];
        Unindexed(entry.Kind).Add(entry.Object!);
        Release(slot);
    }

    private List<IWorldObject> Unindexed(SpatialKind kind) =>
        kind == SpatialKind.Creature ? _unindexedCreatures : _unindexedCharacters;

    private static ref SpatialHandle HandleOf(IWorldObject obj)
    {
        if (obj is Creature creature)
            return ref creature.Spatial;
        if (obj is CharacterEntity character)
            return ref character.Spatial;
        return ref Unsafe.NullRef<SpatialHandle>();
    }

    private static bool Contains(List<IWorldObject> list, IWorldObject obj)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], obj))
                return true;
        }

        return false;
    }

    private static bool RemoveReference(List<IWorldObject> list, IWorldObject obj)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], obj))
            {
                list.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    private struct Entry
    {
        public float X;
        public float Z;
        public ulong RawGuid;
        public IWorldObject? Object;
        public int Cell;
        public int Next;
        public int Prev;

        // A character's place in _characterSlots.
        public int CharacterIndex;

        // Where the instance last recorded the entity's position history, on X and Z, for a move's step (#1082).
        public float RecordedX;
        public float RecordedZ;

        // Its place in _moved plus one; 0 while it has not moved since the last RecordMoved (#1082).
        public int MovedIndex;
        public SpatialKind Kind;

        // A jump since the last RecordMoved: the next move is measured from where it lands (#1082).
        public bool Jumped;
    }
}
