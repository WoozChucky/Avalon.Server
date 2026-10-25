using Avalon.Combat;
using Avalon.Domain.World;

namespace Avalon.World.Characters;

/// <summary>
/// What a character's Resonance counts were last counted from (#1180): the table and, in the walk's order, each worn
/// item's modifier list, by reference. The counts are a function of exactly these, and a held item's list is never
/// changed in place (<see cref="Inventory.ItemModifierStore" />: a change to an item's modifiers puts a new list), so a
/// refresh that finds the same table and the same lists keeps the counts it has instead of counting them again: an
/// aura, a level-up or anything else that moves no gear costs one reference compare per worn item. Anything that does
/// move the count (an item put on or taken off, a template edit that no longer fits a slot, a modifier change, a reload
/// that changes the table) changes a reference here, so that refresh counts. The array grows only when the character
/// wears more items than it ever has; it holds the lists it names, so none is collected and its reference reused while
/// it is held. World-side, one per character. Tick thread only.
/// </summary>
internal sealed class ResonanceInputs
{
    private List<ItemInstanceModifier>?[] _modifiers = [];
    private int _count;
    private ResonanceTable? _table;

    /// <summary>
    /// True when <paramref name="worn" /> over <paramref name="table" /> is what the counts were last counted from. False
    /// forgets what was held, so a count that throws before <see cref="Remember" /> is never kept as a finished one.
    /// </summary>
    public bool Match(ResonanceTable table, List<WornItem> worn)
    {
        if (ReferenceEquals(table, _table) && worn.Count == _count && SameLists(worn))
            return true;

        _table = null;
        return false;
    }

    /// <summary>The counts were just counted from <paramref name="worn" /> over <paramref name="table" />.</summary>
    public void Remember(ResonanceTable table, List<WornItem> worn)
    {
        if (_modifiers.Length < worn.Count)
            _modifiers = new List<ItemInstanceModifier>?[worn.Count];

        for (int i = 0; i < worn.Count; i++)
            _modifiers[i] = worn[i].Modifiers;

        // Lists no longer worn are let go.
        Array.Clear(_modifiers, worn.Count, _modifiers.Length - worn.Count);
        _count = worn.Count;
        _table = table;
    }

    private bool SameLists(List<WornItem> worn)
    {
        for (int i = 0; i < worn.Count; i++)
        {
            if (!ReferenceEquals(worn[i].Modifiers, _modifiers[i]))
                return false;
        }

        return true;
    }
}
