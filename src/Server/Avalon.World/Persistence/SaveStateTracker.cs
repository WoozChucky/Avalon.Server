using Avalon.Common;
using Avalon.Common.ValueObjects;

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

/// <summary>
/// An entry's state and the version it was given when last marked. <paramref name="Modifiers" />, on an item's mark
/// only (#1179): its rolled rows (modifiers and base stats) changed, so the save that upserts it rewrites them all.
/// </summary>
public readonly record struct SaveMark(SaveState State, long Version, bool Modifiers);

/// <summary>
/// What one save carried, taken on the tick thread. Acknowledging it clears exactly these entries,
/// and only those whose version has not moved since.
/// </summary>
public sealed record SaveMarks(
    IReadOnlyDictionary<ItemInstanceId, SaveMark> Items,
    IReadOnlyDictionary<(InventoryType Container, byte Slot), SaveMark> Slots,
    long? GoldVersion,
    long? StatsVersion = null,
    long? PvpVersion = null,
    IReadOnlyDictionary<uint, SaveMark>? Quests = null,
    IReadOnlyDictionary<uint, SaveMark>? Ignores = null,
    long? AurasVersion = null);

/// <summary>
/// The per-character save state of every item instance and inventory slot, and gold's dirty flag.
/// Tick thread only. An absent entry is Unchanged.
/// </summary>
public sealed class SaveStateTracker
{
    private readonly Dictionary<ItemInstanceId, SaveMark> _items = [];
    private readonly Dictionary<(InventoryType Container, byte Slot), SaveMark> _slots = [];
    private readonly Dictionary<uint, SaveMark> _quests = [];
    private readonly Dictionary<uint, SaveMark> _ignores = [];
    private long _version;
    private long _goldVersion;
    private long _statsVersion;
    private long _pvpVersion;
    private long _aurasVersion;

    public bool GoldDirty { get; private set; }

    /// <summary>The derived stats changed (spec #463): the next save writes the CharacterStats row.</summary>
    public bool StatsDirty { get; private set; }

    /// <summary>The PvP flag or its timer changed (#164). The row is written whole by every save; this keeps HasChanges honest.</summary>
    public bool PvpDirty { get; private set; }

    /// <summary>An aura was applied, refreshed, stacked or removed (auras): the next save rewrites them all.</summary>
    public bool AurasDirty { get; private set; }

    public bool HasChanges => _items.Count > 0 || _slots.Count > 0 || GoldDirty || StatsDirty || PvpDirty || _quests.Count > 0
        || _ignores.Count > 0 || AurasDirty;

    public SaveState ItemState(ItemInstanceId id) =>
        _items.TryGetValue(id, out SaveMark mark) ? mark.State : SaveState.Unchanged;

    public SaveState SlotState(InventoryType container, byte slot) =>
        _slots.TryGetValue((container, slot), out SaveMark mark) ? mark.State : SaveState.Unchanged;

    /// <summary>The rolled-rows flag stays as it was (#1179), as it does on every item mark: see <see cref="ItemModifiersChanged" />.</summary>
    public void ItemCreated(ItemInstanceId id) => _items[id] = new SaveMark(SaveState.New, ++_version, ModifiersMarked(id));

    /// <summary>A New item stays New: it has never been written, and its first write carries the change.</summary>
    public void ItemChanged(ItemInstanceId id)
    {
        SaveMark current = _items.GetValueOrDefault(id);
        _items[id] = new SaveMark(ChangedFrom(current.State), ++_version, current.Modifiers);
    }

    /// <summary>
    /// The item's rolled rows (modifiers and base stats) changed, were added or removed (#1179): marked as
    /// <see cref="ItemChanged" /> marks it, and the save that upserts it rewrites its whole current set of both.
    /// Every later mark of the item keeps the flag until a save that carried it is acknowledged; a save that deletes
    /// the item writes none, its rows going with the item's.
    /// </summary>
    public void ItemModifiersChanged(ItemInstanceId id) =>
        _items[id] = new SaveMark(ChangedFrom(ItemState(id)), ++_version, Modifiers: true);

    /// <summary>
    /// Removed even when the item was New and never saved: its insert may already be in flight, and
    /// deleting a row that never landed does nothing.
    /// </summary>
    public void ItemRemoved(ItemInstanceId id) => _items[id] = new SaveMark(SaveState.Removed, ++_version, ModifiersMarked(id));

    /// <summary>
    /// Forgets the item's whole mark, its rolled-rows flag with it (#1175, #1179). A trade calls it after taking the
    /// item out of the giver: the paired save already wrote it, and its rolled rows, as the receiver's, and the fence
    /// would refuse any later save of the giver that still named it.
    /// </summary>
    public void ItemHandedOver(ItemInstanceId id) => _items.Remove(id);

    public void SlotChanged(InventoryType container, byte slot, bool occupiedBefore, bool occupiedAfter)
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
            _slots[(container, slot)] = new SaveMark(state, ++_version, Modifiers: false);
    }

    public void GoldChanged()
    {
        GoldDirty = true;
        _goldVersion = ++_version;
    }

    public void StatsChanged()
    {
        StatsDirty = true;
        _statsVersion = ++_version;
    }

    public void PvpChanged()
    {
        PvpDirty = true;
        _pvpVersion = ++_version;
    }

    public void AurasChanged()
    {
        AurasDirty = true;
        _aurasVersion = ++_version;
    }

    /// <summary>A quest's row changed, was added, removed or completed (#433): the next save rewrites that quest.</summary>
    public void QuestChanged(uint questId) => _quests[questId] = new SaveMark(SaveState.Changed, ++_version, Modifiers: false);

    /// <summary>
    /// The ignore entry naming this character was added or removed (#723): the next save rewrites it from memory.
    /// </summary>
    public void IgnoreChanged(uint ignoredCharacterId) =>
        _ignores[ignoredCharacterId] = new SaveMark(SaveState.Changed, ++_version, Modifiers: false);

    private static SaveState ChangedFrom(SaveState state) => state == SaveState.New ? SaveState.New : SaveState.Changed;

    private bool ModifiersMarked(ItemInstanceId id) => _items.TryGetValue(id, out SaveMark mark) && mark.Modifiers;

    /// <summary>A copy of every non-Unchanged entry. Later changes do not reach it.</summary>
    public SaveMarks TakeMarks() => new(
        new Dictionary<ItemInstanceId, SaveMark>(_items),
        new Dictionary<(InventoryType Container, byte Slot), SaveMark>(_slots),
        GoldDirty ? _goldVersion : null,
        StatsDirty ? _statsVersion : null,
        PvpDirty ? _pvpVersion : null,
        new Dictionary<uint, SaveMark>(_quests),
        new Dictionary<uint, SaveMark>(_ignores),
        AurasDirty ? _aurasVersion : null);

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

        foreach (((InventoryType Container, byte Slot) key, SaveMark mark) in marks.Slots)
        {
            if (_slots.TryGetValue(key, out SaveMark current) && current.Version == mark.Version)
                _slots.Remove(key);
        }

        if (marks.GoldVersion is { } version && GoldDirty && _goldVersion == version)
            GoldDirty = false;

        if (marks.StatsVersion is { } statsVersion && StatsDirty && _statsVersion == statsVersion)
            StatsDirty = false;

        if (marks.PvpVersion is { } pvpVersion && PvpDirty && _pvpVersion == pvpVersion)
            PvpDirty = false;

        if (marks.Quests is { } quests)
        {
            foreach ((uint id, SaveMark mark) in quests)
            {
                if (_quests.TryGetValue(id, out SaveMark current) && current.Version == mark.Version)
                    _quests.Remove(id);
            }
        }

        if (marks.Ignores is { } ignores)
        {
            foreach ((uint id, SaveMark mark) in ignores)
            {
                if (_ignores.TryGetValue(id, out SaveMark current) && current.Version == mark.Version)
                    _ignores.Remove(id);
            }
        }

        if (marks.AurasVersion is { } aurasVersion && AurasDirty && _aurasVersion == aurasVersion)
            AurasDirty = false;
    }
}
