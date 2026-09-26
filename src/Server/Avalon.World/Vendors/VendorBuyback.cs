using System.Diagnostics.CodeAnalysis;
using Avalon.World.Public.Characters;

namespace Avalon.World.Vendors;

/// <summary>
/// One sale that can be undone: the exact instance sold (same id, count, durability, flags,
/// charges), and the copper it sold for, which is the price to buy it back.
/// </summary>
public sealed record BuybackEntry(InventoryItem Item, ulong Price);

/// <summary>
/// A session's last <see cref="Capacity" /> sales, newest at index 0 (spec #432). The eleventh
/// pushes the oldest out, and an item pushed out is gone. In memory only, cleared on despawn, so a
/// buyback lost on logout is simply a completed sale. Tick thread only.
/// </summary>
public sealed class VendorBuyback
{
    public const int Capacity = 10;

    private readonly List<BuybackEntry> _entries = new(Capacity + 1);

    public IReadOnlyList<BuybackEntry> Entries => _entries;

    public void Push(BuybackEntry entry)
    {
        _entries.Insert(0, entry);
        if (_entries.Count > Capacity)
            _entries.RemoveAt(_entries.Count - 1);
    }

    /// <summary>The entry at an index a client sent, or false for any index past the list.</summary>
    public bool TryGet(uint index, [NotNullWhen(true)] out BuybackEntry? entry)
    {
        if (index < (uint)_entries.Count)
        {
            entry = _entries[(int)index];
            return true;
        }

        entry = null;
        return false;
    }

    public void RemoveAt(uint index) => _entries.RemoveAt(checked((int)index));

    public void Clear() => _entries.Clear();
}
