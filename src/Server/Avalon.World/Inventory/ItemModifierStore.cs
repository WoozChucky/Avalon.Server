using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Public.Characters;
using Avalon.World.Reload;

namespace Avalon.World.Inventory;

/// <summary>
/// The modifiers of one character's items (#1179), keyed by item id: World-side, so the modding API, whose
/// <see cref="Public.Characters.InventoryItem" /> is all it sees of an item, never reaches them. Loaded at select, and
/// otherwise written only by <see cref="CharacterInventoryService" />, beside the container it changes, so an entry
/// lives exactly as long as its item is held. Each entry also holds the name its modifiers give a magic copy
/// (<see cref="DescribedName" />), worked out at the copy's first describe in an items generation. An item outside every
/// container (taken out, in the buyback list, in a trade's plan) carries its list as a <see cref="CarriedItem" />. Tick
/// thread only.
/// </summary>
public sealed class ItemModifierStore
{
    private readonly Dictionary<ItemInstanceId, Entry> _byItem = [];

    /// <summary>The item's live list, in ordinal order, or an empty one. Saves copy it; nothing else may change it.</summary>
    /// <remarks>Never allocates: an item without modifiers answers the one empty array.</remarks>
    public IReadOnlyList<ItemInstanceModifier> Of(ItemInstanceId item) =>
        _byItem.TryGetValue(item, out Entry entry) ? entry.Modifiers : Array.Empty<ItemInstanceModifier>();

    /// <summary>
    /// The item's live list itself, in ordinal order, or null for an item without modifiers: what the stats refresh folds,
    /// read through the concrete list. Nothing may change it.
    /// </summary>
    internal List<ItemInstanceModifier>? ListOf(ItemInstanceId item) =>
        _byItem.TryGetValue(item, out Entry entry) ? entry.Modifiers : null;

    /// <summary>
    /// The name <paramref name="item" /> is described by in <paramref name="items" /> (<see cref="ItemSlotDtoMapper.NameOf" />):
    /// a magic copy's is held on its entry, so only its first describe in a generation builds it.
    /// </summary>
    public string? NameOf(InventoryItem item, ItemsPatch items)
    {
        ref Entry entry = ref CollectionsMarshal.GetValueRefOrNullRef(_byItem, item.InstanceId);
        return Unsafe.IsNullRef(ref entry)
            ? ItemSlotDtoMapper.NameOf(item.TemplateId, item.Rarity, item.RareName, Array.Empty<ItemInstanceModifier>(), items)
            : entry.Name.Of(item.TemplateId, item.Rarity, item.RareName, entry.Modifiers, items);
    }

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

            _byItem[instance.Id] = new Entry(modifiers);
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

        _byItem[item] = new Entry([.. modifiers]);
    }

    /// <summary>
    /// A copy made just now arrives with the modifiers its roll made for it alone (#1179, RolledItem.Modifiers): the roll
    /// hands that list over, so it becomes the item's own entry, not copied, and a making allocates its rows and their
    /// lists only. Nothing for none.
    /// </summary>
    internal void PutMade(ItemInstanceId item, List<ItemInstanceModifier> made)
    {
        if (made.Count == 0)
        {
            _byItem.Remove(item);
            return;
        }

        _byItem[item] = new Entry(made);
    }

    /// <summary>The item leaves: its list goes with it, and the entry is gone. An empty list for an item without one.</summary>
    internal IReadOnlyList<ItemInstanceModifier> Take(ItemInstanceId item) =>
        _byItem.Remove(item, out Entry entry) ? entry.Modifiers : Array.Empty<ItemInstanceModifier>();

    /// <summary>One held item's modifiers and the name they give it; a struct in the dictionary, so an entry costs no object.</summary>
    private struct Entry(List<ItemInstanceModifier> modifiers)
    {
        public readonly List<ItemInstanceModifier> Modifiers = modifiers;
        public DescribedName Name;
    }
}
