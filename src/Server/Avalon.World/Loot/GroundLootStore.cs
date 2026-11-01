using System.Diagnostics.CodeAnalysis;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Instances;

namespace Avalon.World.Loot;

/// <summary>What a <see cref="GroundLootStore.Visit{TVisitor}" /> walk is handed, one drop at a time.</summary>
internal interface ILootVisitor
{
    void Visit(GroundLoot drop);
}

/// <summary>
/// The drops on the ground in one instance. Tick thread only, like the rest of the instance.
/// </summary>
/// <remarks>
/// Each drop is also filed by where it lies (#1269), in the cells of the instance's spatial grid (<see cref="GridCells" />),
/// so a client's loot view visits the drops of the cells around its character instead of every drop of the instance.
/// Each cell is a list linked through its drops, so adding and removing a drop allocates nothing. A drop whose X or Z is
/// not finite is kept but in no cell, as no distance finds it within reach.
/// </remarks>
public sealed class GroundLootStore
{
    // Keyed by the raw value, not the ObjectGuid: ObjectGuid is a mutable class, and a Set on the
    // object used as a key would strand its entry.
    private readonly Dictionary<ulong, GroundLoot> _drops = [];
    private readonly GridCells _cells;

    // The first drop of each cell's list, null for an empty cell.
    private readonly GroundLoot?[] _heads;

    /// <summary>A store with one cell, for an instance that has no layout (tests).</summary>
    public GroundLootStore() : this(GridCells.One)
    {
    }

    /// <summary>A store filing its drops in <paramref name="cells" />, the instance's grid.</summary>
    internal GroundLootStore(GridCells cells)
    {
        _cells = cells;
        _heads = new GroundLoot?[cells.Count];
    }

    public int Count => _drops.Count;

    /// <summary>Every drop: the store's own dictionary's values, which a caller may walk with their struct enumerator.</summary>
    public IReadOnlyCollection<GroundLoot> All => _drops.Values;

    public void Add(GroundLoot drop)
    {
        _drops.Add(drop.Guid.RawValue, drop);

        Vector3 position = drop.Position;
        if (!float.IsFinite(position.x) || !float.IsFinite(position.z))
            return;

        int cell = _cells.Row(position.z) * _cells.Columns + _cells.Column(position.x);
        GroundLoot? head = _heads[cell];
        drop.Cell = cell;
        drop.PreviousInCell = null;
        drop.NextInCell = head;
        if (head is not null)
            head.PreviousInCell = drop;
        _heads[cell] = drop;
    }

    public bool TryGet(ObjectGuid guid, [NotNullWhen(true)] out GroundLoot? drop) => _drops.TryGetValue(guid.RawValue, out drop);

    public bool Remove(ObjectGuid guid)
    {
        if (!_drops.Remove(guid.RawValue, out GroundLoot? drop))
            return false;

        Unlink(drop);
        return true;
    }

    public void Clear()
    {
        foreach (GroundLoot drop in _drops.Values)
            Unfiled(drop);

        Array.Clear(_heads);
        _drops.Clear();
    }

    /// <summary>
    /// Hands <paramref name="visitor" /> every drop within <paramref name="reach" /> of (x, z) on X/Z, each once: the drops
    /// of the cells the circle's box overlaps, each kept only when its squared distance is at most the squared reach, as
    /// <c>DoodadIndex.Visit</c> finds doodads. A centre or a reach that is not a number, or a negative reach, finds none;
    /// an infinite reach finds every drop in a cell. The visitor must not add or remove drops. Allocates nothing.
    /// </summary>
    internal void Visit<TVisitor>(float x, float z, float reach, ref TVisitor visitor)
        where TVisitor : struct, ILootVisitor
    {
        if (!float.IsFinite(x) || !float.IsFinite(z) || !(reach >= 0f) || _drops.Count == 0)
            return;

        // Widened as the doodad index widens its reach: more than any rounding of a distance or a cell index; the
        // distance test below is the exact one. An infinite reach widens to infinity, which the clamps turn into every cell.
        float widened = reach * (1f + 1f / 1024f) + 0.25f;
        float reachSquared = reach * reach;
        int firstColumn = _cells.Column(x - widened);
        int lastColumn = _cells.Column(x + widened);
        int lastRow = _cells.Row(z + widened);
        for (int row = _cells.Row(z - widened); row <= lastRow; row++)
        {
            int rowStart = row * _cells.Columns;
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                for (GroundLoot? drop = _heads[rowStart + column]; drop is not null; drop = drop.NextInCell)
                {
                    Vector3 position = drop.Position;
                    float dx = position.x - x;
                    float dz = position.z - z;
                    if (dx * dx + dz * dz <= reachSquared)
                        visitor.Visit(drop);
                }
            }
        }
    }

    private void Unlink(GroundLoot drop)
    {
        if (drop.Cell < 0)
            return;

        if (drop.PreviousInCell is { } previous)
            previous.NextInCell = drop.NextInCell;
        else
            _heads[drop.Cell] = drop.NextInCell;

        if (drop.NextInCell is { } next)
            next.PreviousInCell = drop.PreviousInCell;

        Unfiled(drop);
    }

    private static void Unfiled(GroundLoot drop)
    {
        drop.Cell = -1;
        drop.NextInCell = null;
        drop.PreviousInCell = null;
    }
}
