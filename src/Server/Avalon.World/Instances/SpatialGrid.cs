using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.ChunkLayouts;
using Avalon.World.Entities;
using Avalon.World.Public;

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
/// </remarks>
internal sealed class SpatialGrid
{
    private const int InitialCapacity = 16;
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

    private Entry[] _entries = new Entry[InitialCapacity];
    private int _used;
    private int _free = Unlinked;

    // Members that are not indexed (another type, or indexed by another instance's grid), visited by every query.
    private readonly List<IWorldObject> _unindexedCreatures = [];
    private readonly List<IWorldObject> _unindexedCharacters = [];

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
        Link(slot, CellOf(position));
        handle.Grid = this;
        handle.Slot = slot;
    }

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
    /// is relinked only when its cell changed. Called by the concrete position setters, on every write.
    /// </summary>
    public void Moved(int slot, Vector3 position)
    {
        ref Entry entry = ref _entries[slot];
        entry.X = position.x;
        entry.Z = position.z;
        int cell = CellOf(position);
        if (cell != entry.Cell)
        {
            Unlink(slot);
            Link(slot, cell);
        }
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

    private void VisitCircle<TVisitor>(float x, float z, float reach, ref TVisitor visitor)
        where TVisitor : struct, ISpatialVisitor
    {
        int lastRow = Row(z + reach);
        float reachSquared = reach * reach;
        for (int row = Row(z - reach); row <= lastRow; row++)
        {
            // How far the row's band is from the centre on Z; the edge rows reach on outwards, as their cells are clamped.
            float bandLow = row == 0 ? float.NegativeInfinity : _originZ + row * _cellSize;
            float bandHigh = row == _rows - 1 ? float.PositiveInfinity : _originZ + (row + 1) * _cellSize;
            float dz = z < bandLow ? bandLow - z : z > bandHigh ? z - bandHigh : 0f;
            if (dz > reach)
                continue;

            float halfWidth = MathF.Sqrt(reachSquared - dz * dz);
            int rowStart = row * _columns;
            int lastColumn = Column(x + halfWidth);
            for (int column = Column(x - halfWidth); column <= lastColumn; column++)
                VisitCell(_heads[rowStart + column], ref visitor);
        }
    }

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
    }

    private void Unlink(int slot)
    {
        ref Entry entry = ref _entries[slot];
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
            Array.Resize(ref _entries, _entries.Length * 2);
        return _used++;
    }

    private void Release(int slot)
    {
        Unlink(slot);
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
        public SpatialKind Kind;
    }
}
