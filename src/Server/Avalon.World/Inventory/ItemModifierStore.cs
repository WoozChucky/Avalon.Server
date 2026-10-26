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
/// (<see cref="DescribedName" />), worked out at the copy's first describe in an items generation, and the copy's hidden
/// instability (#1180), which only an entry holds, since every imprinted copy carries its imprint. An item outside every
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
    /// The item's hidden instability (#1180), the running total of its imprints' rolls; 0 for an item without an entry.
    /// World-side only: never sent, shown, logged or exported.
    /// </summary>
    public ushort InstabilityOf(ItemInstanceId item)
    {
        ref Entry entry = ref CollectionsMarshal.GetValueRefOrNullRef(_byItem, item);
        return Unsafe.IsNullRef(ref entry) ? (ushort)0 : entry.Instability;
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
    /// At select: every loaded item's modifiers, in ordinal order, each one's values in index order, its base stats and its
    /// instability. It keeps the loaded objects and marks nothing: they are what the database holds. Throws, as
    /// <see cref="Put" /> does, for an item carrying instability but no rolled rows, rather than drop a total every later
    /// save of the item would need.
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
            {
                if (instance.Instability > 0)
                    throw new InvalidOperationException($"Item {instance.Id} carries instability but no rolled rows.");

                continue;
            }

            List<ItemInstanceModifier>? modifiers = null;
            if (instance.Modifiers.Count > 0)
            {
                modifiers = new List<ItemInstanceModifier>(instance.Modifiers);
                modifiers.Sort(static (a, b) => a.Ordinal.CompareTo(b.Ordinal));
                foreach (ItemInstanceModifier modifier in modifiers)
                    modifier.Values.Sort(static (a, b) => a.Index.CompareTo(b.Index));
            }

            _byItem[instance.Id] = new Entry(modifiers, baseStats, instance.Instability);
        }
    }

    /// <summary>
    /// The item arrives with <paramref name="baseStats" />, <paramref name="modifiers" /> and its
    /// <paramref name="instability" /> (#1180): a copy of the list is kept, and nothing for none of either. Throws for an
    /// instability above 0 with no rolled rows: an imprinted item always carries its imprint.
    /// </summary>
    internal void Put(ItemInstanceId item, ItemBaseStats baseStats, IReadOnlyList<ItemInstanceModifier> modifiers,
        ushort instability)
    {
        if (!HasRolledRows(baseStats, modifiers))
        {
            if (instability > 0)
                throw new ArgumentException($"Item {item} carries instability but no rolled rows.", nameof(instability));

            _byItem.Remove(item);
            return;
        }

        _byItem[item] = new Entry(modifiers.Count == 0 ? null : [.. modifiers], baseStats, instability);
    }

    /// <summary>
    /// A copy made just now arrives with the base stats and the modifiers its roll made for it alone (#1179, RolledItem):
    /// the roll hands that list over, so it becomes the item's own entry, not copied, and a making allocates its rows and
    /// their lists only. Nothing for none of either. A copy made just now was never imprinted: its instability is 0.
    /// </summary>
    internal void PutMade(ItemInstanceId item, ItemBaseStats baseStats, List<ItemInstanceModifier> made)
    {
        if (!HasRolledRows(baseStats, made))
        {
            _byItem.Remove(item);
            return;
        }

        _byItem[item] = new Entry(made.Count == 0 ? null : made, baseStats, 0);
    }

    /// <summary>
    /// A held item's modifiers change (#1180: a wake): <paramref name="modifiers" /> becomes its list, beside the same base
    /// stats, and the name its modifiers gave it is worked out again at its next describe. A changed modifier is always a
    /// new list and a new row, never a field written inside the list the item holds: a stats refresh recounts Resonance
    /// only for a worn list it has not seen (Characters.ResonanceInputs, by reference). So the list must not be the one
    /// held, and the caller keeps no use of it. Throws for an item without an entry, and for the list it already holds.
    /// The item keeps its instability.
    /// </summary>
    internal void ReplaceModifiers(ItemInstanceId item, List<ItemInstanceModifier> modifiers)
    {
        ref Entry entry = ref EntryToReplace(item, modifiers);
        entry = new Entry(modifiers, entry.BaseStats, entry.Instability);
    }

    /// <summary>
    /// As the overload above, and the item's instability becomes <paramref name="instability" /> (#1180: an imprint's new
    /// total, beside the list it made).
    /// </summary>
    internal void ReplaceModifiers(ItemInstanceId item, List<ItemInstanceModifier> modifiers, ushort instability)
    {
        ref Entry entry = ref EntryToReplace(item, modifiers);
        entry = new Entry(modifiers, entry.BaseStats, instability);
    }

    private ref Entry EntryToReplace(ItemInstanceId item, List<ItemInstanceModifier> modifiers)
    {
        ref Entry entry = ref CollectionsMarshal.GetValueRefOrNullRef(_byItem, item);
        if (Unsafe.IsNullRef(ref entry))
            throw new InvalidOperationException($"Item {item} carries no modifiers to replace.");

        if (ReferenceEquals(entry.Modifiers, modifiers))
            throw new ArgumentException($"Item {item}'s modifiers are replaced by a new list, never by the one it holds.", nameof(modifiers));

        return ref entry;
    }

    /// <summary>
    /// The item leaves: its base stats, its list and its instability go with it, and the entry is gone. None, an empty list
    /// and 0 for an item without them.
    /// </summary>
    internal (ItemBaseStats BaseStats, IReadOnlyList<ItemInstanceModifier> Modifiers, ushort Instability) Take(ItemInstanceId item) =>
        _byItem.Remove(item, out Entry entry)
            ? (entry.BaseStats, ModifiersOf(entry), entry.Instability)
            : (default, Array.Empty<ItemInstanceModifier>(), (ushort)0);

    private static readonly ItemBaseStats s_noBaseStats = default;

    private static IReadOnlyList<ItemInstanceModifier> ModifiersOf(in Entry entry) =>
        entry.Modifiers is { } modifiers ? modifiers : Array.Empty<ItemInstanceModifier>();

    /// <summary>
    /// One held item's modifiers (null for none), its base stats, its hidden instability (#1180) and the name they give it;
    /// a struct in the dictionary, so an entry costs no object.
    /// </summary>
    private struct Entry(List<ItemInstanceModifier>? modifiers, ItemBaseStats baseStats, ushort instability)
    {
        public readonly List<ItemInstanceModifier>? Modifiers = modifiers;
        public readonly ItemBaseStats BaseStats = baseStats;
        public readonly ushort Instability = instability;
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
