using Avalon.Common.ValueObjects;
using Avalon.Domain.World;

namespace Avalon.World.Inventory;

/// <summary>
/// The modifiers of one character's items (#1179), keyed by item id: World-side, so the modding API, whose
/// <see cref="Public.Characters.InventoryItem" /> is all it sees of an item, never reaches them. Loaded at select, and
/// otherwise written only by <see cref="CharacterInventoryService" />, beside the container it changes, so an entry
/// lives exactly as long as its item is held. An item outside every container (taken out, in the buyback list, in a
/// trade's plan) carries its list as a <see cref="CarriedItem" />. Tick thread only.
/// </summary>
public sealed class ItemModifierStore
{
    private readonly Dictionary<ItemInstanceId, List<ItemInstanceModifier>> _byItem = [];

    /// <summary>The item's live list, in ordinal order, or an empty one. Saves copy it; nothing else may change it.</summary>
    /// <remarks>Never allocates: an item without modifiers answers the one empty array.</remarks>
    public IReadOnlyList<ItemInstanceModifier> Of(ItemInstanceId item) =>
        _byItem.TryGetValue(item, out List<ItemInstanceModifier>? modifiers) ? modifiers : Array.Empty<ItemInstanceModifier>();

    /// <summary>
    /// At select: every loaded item's modifiers, in ordinal order, each one's values in index order. It keeps the loaded
    /// objects and marks nothing: they are what the database holds.
    /// </summary>
    public void Load(IReadOnlyCollection<ItemInstance> instances)
    {
        _byItem.Clear();
        foreach (ItemInstance instance in instances)
        {
            if (instance.Modifiers.Count == 0)
                continue;

            var modifiers = new List<ItemInstanceModifier>(instance.Modifiers);
            modifiers.Sort(static (a, b) => a.Ordinal.CompareTo(b.Ordinal));
            foreach (ItemInstanceModifier modifier in modifiers)
                modifier.Values.Sort(static (a, b) => a.Index.CompareTo(b.Index));

            _byItem[instance.Id] = modifiers;
        }
    }

    /// <summary>The item arrives with <paramref name="modifiers" />: a copy of the list is kept, and nothing for none.</summary>
    internal void Put(ItemInstanceId item, IReadOnlyList<ItemInstanceModifier> modifiers)
    {
        if (modifiers.Count == 0)
        {
            _byItem.Remove(item);
            return;
        }

        _byItem[item] = [.. modifiers];
    }

    /// <summary>The item leaves: its list goes with it, and the entry is gone. An empty list for an item without one.</summary>
    internal IReadOnlyList<ItemInstanceModifier> Take(ItemInstanceId item) =>
        _byItem.Remove(item, out List<ItemInstanceModifier>? modifiers) ? modifiers : Array.Empty<ItemInstanceModifier>();
}
