namespace Avalon.Domain.World;

/// <summary>How many worn items carry an affix of a Resonance theme (#1180): the theme's id, never its slot.</summary>
public readonly record struct ThemeCount(int ThemeId, int Count);

/// <summary>
/// One count per Resonance theme of a <see cref="ResonanceTable" /> (#1180): how many worn items carry at least one prefix
/// or suffix of that theme. A reusable buffer, one int per slot of the table it is bound to (<see cref="Use" />), which the
/// stats fold fills item by item (<see cref="AddItem" />). It is reallocated only when it is bound to a table of another
/// size, so a refresh allocates nothing. Not thread-safe: a character's buffers are the tick's.
/// </summary>
public sealed class ResonanceCounts
{
    private int[] _counts = [];

    // Per slot, the number of the item that last counted it (AddItem), beside the counts and always their size: an item
    // counts a slot once, with one table lookup per modifier. Scratch for the count in progress, never compared or copied.
    // _item is the number of the item being added; it starts at 0 with every stamp, so a zero stamp never matches it.
    private int[] _stamps = [];
    private int _item;

    /// <summary>The table the counts are slots of; <see cref="ResonanceTable.Empty" /> until bound.</summary>
    public ResonanceTable Table { get; private set; } = ResonanceTable.Empty;

    /// <summary>
    /// Binds the counts to <paramref name="table" />. The buffer is reallocated only when the table's number of themes
    /// differs from its length; bound to another table, the counts start at zero, since its slots are not the old ones.
    /// </summary>
    public void Use(ResonanceTable table)
    {
        if (ReferenceEquals(table, Table))
            return;

        Table = table;
        if (_counts.Length != table.Count)
        {
            _counts = new int[table.Count];
            _stamps = new int[table.Count];
            _item = 0;
        }
        else
        {
            Clear();
        }
    }

    /// <summary>Every count back to zero, for a new count.</summary>
    public void Clear()
    {
        Array.Clear(_counts);
        Array.Clear(_stamps);
        _item = 0;
    }

    /// <summary>
    /// The one per-item step of the count: adds 1 to each theme <paramref name="modifiers" /> (one worn item's, null for
    /// none) carries, once per item however many of its affixes share that theme. Only prefixes and suffixes count, through
    /// the table; an implicit or a unique line has no theme. Reads the list by index, one table lookup per modifier, and
    /// allocates nothing: the item stamps each slot it counts with its number, so a later affix of that theme on the same
    /// item finds the slot stamped.
    /// </summary>
    public void AddItem(List<ItemInstanceModifier>? modifiers)
    {
        if (modifiers is null)
            return;

        int item = ++_item;
        ResonanceTable table = Table;
        for (int i = 0; i < modifiers.Count; i++)
        {
            // An affix that does not count is skipped here, before its slot is stamped, so it never hides a later affix
            // of its theme on the same item: part B's sealed affix (`if (modifier.Sealed) continue;`) goes first.
            ItemInstanceModifier modifier = modifiers[i];
            if (!table.TrySlotOfModifier(modifier.ModifierId, out int slot) || _stamps[slot] == item)
                continue;

            _stamps[slot] = item;
            _counts[slot]++;
        }
    }

    /// <summary>The count of the theme, or 0 when the table does not hold it.</summary>
    public int Of(int themeId) => Table.TrySlotOfTheme(themeId, out int slot) ? _counts[slot] : 0;

    /// <summary>True when both hold no count at all, or when they share a table and hold the same counts.</summary>
    public bool SameAs(ResonanceCounts other)
    {
        if (ReferenceEquals(Table, other.Table))
            return _counts.AsSpan().SequenceEqual(other._counts);

        return IsZero() && other.IsZero();
    }

    /// <summary>Becomes <paramref name="source" />: its table and its counts.</summary>
    public void CopyFrom(ResonanceCounts source)
    {
        Use(source.Table);
        source._counts.AsSpan().CopyTo(_counts);
    }

    /// <summary>Each theme whose count is above 0, by slot.</summary>
    public Enumerator GetEnumerator() => new(this);

    private bool IsZero() => !_counts.AsSpan().ContainsAnyExcept(0);

    /// <summary>The themes above 0, by slot, as <see cref="ThemeCount" />s; a struct, so a foreach allocates nothing.</summary>
    public struct Enumerator
    {
        private readonly int[] _counts;
        private readonly ResonanceTable _table;
        private int _slot;

        internal Enumerator(ResonanceCounts counts)
        {
            _counts = counts._counts;
            _table = counts.Table;
            _slot = -1;
        }

        public readonly ThemeCount Current => new(_table.ThemeIdAt(_slot), _counts[_slot]);

        public bool MoveNext()
        {
            while (++_slot < _counts.Length)
            {
                if (_counts[_slot] > 0)
                    return true;
            }

            return false;
        }
    }
}
