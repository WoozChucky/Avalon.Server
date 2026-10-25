using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalon.Combat;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;
using Avalon.World.Public.Characters;
using Avalon.World.Reload;

namespace Avalon.World.Inventory;

/// <summary>
/// The rolled rows of one character's items (#1179), keyed by item id: each copy's modifiers and its base stats.
/// World-side, so the modding API, whose <see cref="Public.Characters.InventoryItem" /> is all it sees of an item, never
/// reaches them. Loaded at select, and otherwise written only by <see cref="CharacterInventoryService" />, beside the
/// container it changes, so an entry lives exactly as long as its item is held; a copy has an entry when it has
/// modifiers or base stats, and is then one of a kind. Each entry also holds the name its modifiers give a magic copy
/// (<see cref="DescribedName" />), worked out at the copy's first describe in an items generation. An item outside every
/// container (taken out, in the buyback list, in a trade's plan) carries its rows as a <see cref="CarriedItem" />. Tick
/// thread only.
/// </summary>
public sealed class ItemModifierStore
{
    private readonly Dictionary<ItemInstanceId, Entry> _byItem = [];

    /// <summary>The item's live list, in ordinal order, or an empty one. Saves copy it; nothing else may change it.</summary>
    /// <remarks>Never allocates: an item without modifiers answers the one empty array.</remarks>
    public IReadOnlyList<ItemInstanceModifier> Of(ItemInstanceId item)
    {
        ref Entry entry = ref CollectionsMarshal.GetValueRefOrNullRef(_byItem, item);
        return Unsafe.IsNullRef(ref entry) ? Array.Empty<ItemInstanceModifier>() : ModifiersOf(entry);
    }

    /// <summary>The item's rolled base stats, by value; none (default) for an item without them.</summary>
    public ItemBaseStats BaseStatsOf(ItemInstanceId item)
    {
        ref Entry entry = ref CollectionsMarshal.GetValueRefOrNullRef(_byItem, item);
        return Unsafe.IsNullRef(ref entry) ? default : entry.BaseStats;
    }

    /// <summary>
    /// Whether the item carries rolled rows, modifiers or base stats: such a copy is one of a kind, so it never stacks or
    /// splits, and is sold and taken out whole.
    /// </summary>
    public bool IsOneOfAKind(ItemInstanceId item) => _byItem.ContainsKey(item);

    /// <summary>
    /// The one rule for whether a copy carries rolled rows (#1179): it has modifiers or base stats. A copy that does has
    /// an entry here, is one of a kind and is marked for the save that writes them; one that has neither has no entry.
    /// </summary>
    internal static bool HasRolledRows(in ItemBaseStats baseStats, IReadOnlyList<ItemInstanceModifier> modifiers) =>
        modifiers.Count > 0 || !baseStats.IsEmpty;

    /// <summary>
    /// The item as the stats refresh folds it, worn as <paramref name="template" /> dealing <paramref name="damageType" />
    /// (its base's), in one lookup: its base stats by value (none for an item without them) and its live modifier list
    /// itself, in ordinal order, read through the concrete list, or null for an item without modifiers. Nothing may change
    /// the list.
    /// </summary>
    internal WornItem WornOf(ItemTemplate template, DamageType? damageType, ItemInstanceId item)
    {
        ref Entry entry = ref CollectionsMarshal.GetValueRefOrNullRef(_byItem, item);
        return Unsafe.IsNullRef(ref entry)
            ? new WornItem(template, default, null, damageType)
            : new WornItem(template, entry.BaseStats, entry.Modifiers, damageType);
    }

    /// <summary>
    /// A held item as a message describes it, in one lookup (<see cref="Held" />): its live modifier list, its base stats
    /// in place and the name it is described by in <paramref name="items" /> (<see cref="ItemSlotDtoMapper.NameOf" />). A
    /// magic copy's name is held on its entry, so only its first describe in a generation builds it. An unidentified copy
    /// is described by no name (#1179), and its held name is neither read nor filled, so the first describe after it is
    /// identified works its name out.
    /// </summary>
    public Held Describe(InventoryItem item, ItemsPatch items)
    {
        ref Entry entry = ref CollectionsMarshal.GetValueRefOrNullRef(_byItem, item.InstanceId);
        if (Unsafe.IsNullRef(ref entry))
        {
            return new Held(Array.Empty<ItemInstanceModifier>(), in s_noBaseStats,
                ItemVisibility.NameShown(item.Identified)
                    ? ItemSlotDtoMapper.NameOf(item.TemplateId, item.Rarity, item.RareName,
                        Array.Empty<ItemInstanceModifier>(), items)
                    : null);
        }

        IReadOnlyList<ItemInstanceModifier> modifiers = ModifiersOf(entry);
        return new Held(modifiers, in entry.BaseStats, ItemVisibility.NameShown(item.Identified)
            ? entry.Name.Of(item.TemplateId, item.Rarity, item.RareName, modifiers, items)
            : null);
    }

    /// <summary>
    /// At select: every loaded item's modifiers, in ordinal order, each one's values in index order, and its base stats.
    /// It keeps the loaded objects and marks nothing: they are what the database holds.
    /// </summary>
    public void Load(IReadOnlyCollection<ItemInstance> instances)
    {
        _byItem.Clear();
        foreach (ItemInstance instance in instances)
        {
            // By index over the concrete list: the IEnumerable overload of ItemBaseStats.Of boxed its enumerator per copy.
            ItemBaseStats baseStats = default;
            List<ItemInstanceBaseStat> rows = instance.BaseStats;
            for (int i = 0; i < rows.Count; i++)
                baseStats = baseStats.With(rows[i].Stat, rows[i].Value);

            if (!HasRolledRows(baseStats, instance.Modifiers))
                continue;

            List<ItemInstanceModifier>? modifiers = null;
            if (instance.Modifiers.Count > 0)
            {
                modifiers = new List<ItemInstanceModifier>(instance.Modifiers);
                modifiers.Sort(static (a, b) => a.Ordinal.CompareTo(b.Ordinal));
                foreach (ItemInstanceModifier modifier in modifiers)
                    modifier.Values.Sort(static (a, b) => a.Index.CompareTo(b.Index));
            }

            _byItem[instance.Id] = new Entry(modifiers, baseStats);
        }
    }

    /// <summary>
    /// The item arrives with <paramref name="baseStats" /> and <paramref name="modifiers" />: a copy of the list is kept,
    /// and nothing for none of either.
    /// </summary>
    internal void Put(ItemInstanceId item, ItemBaseStats baseStats, IReadOnlyList<ItemInstanceModifier> modifiers)
    {
        if (!HasRolledRows(baseStats, modifiers))
        {
            _byItem.Remove(item);
            return;
        }

        _byItem[item] = new Entry(modifiers.Count == 0 ? null : [.. modifiers], baseStats);
    }

    /// <summary>
    /// A copy made just now arrives with the base stats and the modifiers its roll made for it alone (#1179, RolledItem):
    /// the roll hands that list over, so it becomes the item's own entry, not copied, and a making allocates its rows and
    /// their lists only. Nothing for none of either.
    /// </summary>
    internal void PutMade(ItemInstanceId item, ItemBaseStats baseStats, List<ItemInstanceModifier> made)
    {
        if (!HasRolledRows(baseStats, made))
        {
            _byItem.Remove(item);
            return;
        }

        _byItem[item] = new Entry(made.Count == 0 ? null : made, baseStats);
    }

    /// <summary>
    /// The item leaves: its base stats and its list go with it, and the entry is gone. None and an empty list for an item
    /// without them.
    /// </summary>
    internal (ItemBaseStats BaseStats, IReadOnlyList<ItemInstanceModifier> Modifiers) Take(ItemInstanceId item) =>
        _byItem.Remove(item, out Entry entry)
            ? (entry.BaseStats, ModifiersOf(entry))
            : (default, Array.Empty<ItemInstanceModifier>());

    private static readonly ItemBaseStats s_noBaseStats = default;

    private static IReadOnlyList<ItemInstanceModifier> ModifiersOf(in Entry entry) =>
        entry.Modifiers is { } modifiers ? modifiers : Array.Empty<ItemInstanceModifier>();

    /// <summary>
    /// One held item's modifiers (null for none), its base stats and the name they give it; a struct in the dictionary, so
    /// an entry costs no object.
    /// </summary>
    private struct Entry(List<ItemInstanceModifier>? modifiers, ItemBaseStats baseStats)
    {
        public readonly List<ItemInstanceModifier>? Modifiers = modifiers;
        public readonly ItemBaseStats BaseStats = baseStats;
        public DescribedName Name;
    }

    /// <summary>
    /// One held item as <see cref="Describe" /> found it: read it before the store next changes, since
    /// <see cref="BaseStats" /> refers to the entry in place.
    /// </summary>
    public readonly ref struct Held
    {
        private readonly ref readonly ItemBaseStats _baseStats;

        public Held(IReadOnlyList<ItemInstanceModifier> modifiers, ref readonly ItemBaseStats baseStats, string? name)
        {
            Modifiers = modifiers;
            _baseStats = ref baseStats;
            Name = name;
        }

        /// <summary>The live list, in ordinal order, or the one empty array; nothing may change it.</summary>
        public IReadOnlyList<ItemInstanceModifier> Modifiers { get; }

        /// <summary>The rolled base stats, none (default) for an item without them.</summary>
        public ref readonly ItemBaseStats BaseStats => ref _baseStats;

        /// <summary>The name the item is described by, or null when its template's is the item's.</summary>
        public string? Name { get; }
    }
}
