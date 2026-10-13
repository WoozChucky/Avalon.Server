using System.Collections;

namespace Avalon.World.Reload;

/// <summary>
/// One area's rows with a lookup by key (#883), for a reader that looked a row up by walking them, as a kill does for
/// each item it drops and each level it awards against. The rows and their index are one object, built once from
/// the rows as a prepare read them and published with its patch as one reference: a reload replaces both together, so
/// the index cannot outlive its rows or go stale against them. A row's key is never changed once it is read (a change
/// is a reload, forward-only), and nothing is changed here either, so any thread may read it.
/// </summary>
/// <remarks>
/// The first row of a key answers for it, as the walk it replaces answered; a row without a key is listed and never
/// found. It lists the rows in the order they were read.
/// </remarks>
public sealed class KeyedRows<TKey, TRow> : IReadOnlyList<TRow>
    where TKey : struct
    where TRow : class
{
    private readonly TRow[] _rows;
    private readonly Dictionary<TKey, TRow> _byKey;

    private KeyedRows(IReadOnlyCollection<TRow> rows, Func<TRow, TKey?> keyOf)
    {
        _rows = [.. rows];
        _byKey = new Dictionary<TKey, TRow>(_rows.Length);
        foreach (TRow row in _rows)
        {
            if (keyOf(row) is { } key)
            {
                _byKey.TryAdd(key, row);
            }
        }
    }

    /// <summary><paramref name="rows" /> with their lookup; themselves when they already carry one.</summary>
    public static KeyedRows<TKey, TRow> Over(IReadOnlyCollection<TRow> rows, Func<TRow, TKey?> keyOf) =>
        rows as KeyedRows<TKey, TRow> ?? new KeyedRows<TKey, TRow>(rows, keyOf);

    /// <summary>The first row read with that key, or null.</summary>
    public TRow? Find(TKey key) => _byKey.GetValueOrDefault(key);

    public int Count => _rows.Length;

    public TRow this[int index] => _rows[index];

    public IEnumerator<TRow> GetEnumerator() => ((IEnumerable<TRow>)_rows).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _rows.GetEnumerator();
}
