using Avalon.Common.ValueObjects;
using Avalon.World.Public.Enums;

namespace Avalon.World.Persistence;

/// <summary>
/// What the next save must do with a tracked row: insert it (<see cref="New" />), update it
/// (<see cref="Changed" />), delete it (<see cref="Removed" />), or nothing (<see cref="Unchanged" />).
/// </summary>
public enum SaveState : byte
{
    Unchanged,
    New,
    Changed,
    Removed,
}

/// <summary>An entry's state and the version it was given when last marked.</summary>
public readonly record struct SaveMark(SaveState State, long Version);

/// <summary>
/// What one save carried, taken on the tick thread. Acknowledging it clears exactly these entries,
/// and only those whose version has not moved since.
/// </summary>
public sealed record SaveMarks(
    IReadOnlyDictionary<ItemInstanceId, SaveMark> Items,
    IReadOnlyDictionary<(InventoryType Container, ushort Slot), SaveMark> Slots,
    long? MoneyVersion);

/// <summary>
/// The per-character save state of every item instance and inventory slot, and money's dirty flag.
/// Tick thread only. An absent entry is Unchanged.
/// </summary>
public sealed class SaveStateTracker
{
    private readonly Dictionary<ItemInstanceId, SaveMark> _items = [];
    private readonly Dictionary<(InventoryType Container, ushort Slot), SaveMark> _slots = [];
    private long _version;
    private long _moneyVersion;

    public bool MoneyDirty { get; private set; }

    public bool HasChanges => _items.Count > 0 || _slots.Count > 0 || MoneyDirty;

    public SaveState ItemState(ItemInstanceId id) =>
        _items.TryGetValue(id, out SaveMark mark) ? mark.State : SaveState.Unchanged;

    public SaveState SlotState(InventoryType container, ushort slot) =>
        _slots.TryGetValue((container, slot), out SaveMark mark) ? mark.State : SaveState.Unchanged;

    public void ItemCreated(ItemInstanceId id) => _items[id] = new SaveMark(SaveState.New, ++_version);

    /// <summary>A New item stays New: it has never been written, and its first write carries the change.</summary>
    public void ItemChanged(ItemInstanceId id)
    {
        SaveState next = ItemState(id) == SaveState.New ? SaveState.New : SaveState.Changed;
        _items[id] = new SaveMark(next, ++_version);
    }

    /// <summary>
    /// Removed even when the item was New and never saved: its insert may already be in flight, and
    /// deleting a row that never landed does nothing.
    /// </summary>
    public void ItemRemoved(ItemInstanceId id) => _items[id] = new SaveMark(SaveState.Removed, ++_version);

    public void SlotChanged(InventoryType container, ushort slot, bool occupiedBefore, bool occupiedAfter)
    {
        SaveState? next = SlotState(container, slot) switch
        {
            SaveState.Unchanged => (occupiedBefore, occupiedAfter) switch
            {
                (false, true) => SaveState.New,
                (true, true) => SaveState.Changed,
                (true, false) => SaveState.Removed,
                _ => null,
            },
            SaveState.New => occupiedAfter ? SaveState.New : SaveState.Removed,
            _ => occupiedAfter ? SaveState.Changed : SaveState.Removed,
        };

        if (next is { } state)
            _slots[(container, slot)] = new SaveMark(state, ++_version);
    }

    public void MoneyChanged()
    {
        MoneyDirty = true;
        _moneyVersion = ++_version;
    }

    /// <summary>A copy of every non-Unchanged entry. Later changes do not reach it.</summary>
    public SaveMarks TakeMarks() => new(
        new Dictionary<ItemInstanceId, SaveMark>(_items),
        new Dictionary<(InventoryType Container, ushort Slot), SaveMark>(_slots),
        MoneyDirty ? _moneyVersion : null);

    /// <summary>
    /// Called on the tick thread once the save that took <paramref name="marks" /> has committed.
    /// Entries marked again since keep their newer state for the next save.
    /// </summary>
    public void Acknowledge(SaveMarks marks)
    {
        foreach ((ItemInstanceId id, SaveMark mark) in marks.Items)
        {
            if (_items.TryGetValue(id, out SaveMark current) && current.Version == mark.Version)
                _items.Remove(id);
        }

        foreach (((InventoryType Container, ushort Slot) key, SaveMark mark) in marks.Slots)
        {
            if (_slots.TryGetValue(key, out SaveMark current) && current.Version == mark.Version)
                _slots.Remove(key);
        }

        if (marks.MoneyVersion is { } version && MoneyDirty && _moneyVersion == version)
            MoneyDirty = false;
    }
}
