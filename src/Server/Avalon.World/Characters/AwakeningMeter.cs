using Avalon.Common;
using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Items;
using Avalon.World.Public;
using Avalon.World.Public.Characters;

namespace Avalon.World.Characters;

/// <summary>
/// One character's Awakening meter (#1180): the points its kills and damage earned for the sealed affixes it wears and
/// has not yet written into them. A hit adds to <see cref="PendingMilli" /> and writes nothing else: no row, no save
/// mark, no client change. <see cref="Awakening.Settle" /> moves its whole points into the worn rows. Made once with the
/// character; World-side, never on ICharacter, and never saved, so a fraction of a point left at a logout is lost. Tick
/// thread only.
/// </summary>
/// <remarks>
/// While the character is in the world (<see cref="AwakeningQueue.Attach" />), the meter puts it on the world's
/// <see cref="AwakeningQueue" /> once each time it gets work for the tick's flush (<see cref="Due" /> or
/// <see cref="Unsent" /> set while it is not queued), so the flush visits only those characters and an idle tick costs
/// nothing per connection.
/// </remarks>
public sealed class AwakeningMeter
{
    private bool _due;
    private bool _unsent;

    // The queue the meter puts its character on, the character and the connection it is sent through: set while the
    // character is in the world, none otherwise. Whether it is on the queue now.
    private AwakeningQueue? _queue;
    private CharacterEntity? _owner;
    private bool _queued;

    // The Equipment slots holding an item with a seal that fills, in slot order, as the last recount found them; stale
    // once the Equipment changed, and found again at the next use. Grown to the Equipment's capacity the first time a
    // seal is worn, and reused after.
    private byte[] _sealedSlots = [];
    private int _sealedSlotCount;
    private bool _slotsStale;

    /// <summary>
    /// How many sealed rows <see cref="Awakening.Fills" /> accepts on the worn items, as the last stats refresh that
    /// counted Resonance found them: 0 for a character with none, which makes a hit's meter one compare.
    /// </summary>
    public int SealedWorn { get; private set; }

    /// <summary>
    /// The points earned and not yet settled, in thousandths of a point. A hit's product is under 4.3e16 (a health of at
    /// most <see cref="uint.MaxValue" /> times a weight of at most
    /// <see cref="Configuration.AwakeningConfiguration.MaxWeight" /> times 1000), and a hit removes no more than its
    /// creature's health, so it adds about its weight in points at most: neither overflows 64 bits. A creature whose
    /// current health is above its maximum (the benchmark's dummy, at a maximum of 1) gives more than its weight per hit,
    /// still far from overflowing in any run.
    /// </summary>
    public long PendingMilli { get; private set; }

    /// <summary>Whether the tick's Awakening flush must settle and look for a full bar: set by a kill, a settle that moved progress, or a recount that found a seal.</summary>
    public bool Due
    {
        get => _due;
        internal set
        {
            _due = value;
            if (value)
                QueueIfPending();
        }
    }

    /// <summary>Whether progress moved since the client was last told it.</summary>
    public bool Unsent
    {
        get => _unsent;
        internal set
        {
            _unsent = value;
            if (value)
                QueueIfPending();
        }
    }

    /// <summary>The connection the character is in the world through, which the flush sends to; none outside it.</summary>
    internal IWorldConnection? Connection { get; private set; }

    /// <summary>
    /// <paramref name="life" /> of a creature's <paramref name="maxHealth" /> removed, worth <paramref name="weight" />
    /// points for the whole of it: <c>life × weight × 1000 / maxHealth</c> thousandths, in 64 bits. A creature without a
    /// maximum gives nothing.
    /// </summary>
    public void AddDamage(uint life, uint maxHealth, int weight)
    {
        if (maxHealth == 0)
            return;

        PendingMilli += (long)life * weight * 1000 / maxHealth;
    }

    /// <summary>A credited kill worth <paramref name="weight" /> points; the tick's flush settles it.</summary>
    public void AddKill(int weight)
    {
        PendingMilli += weight * 1000L;
        Due = true;
    }

    /// <summary>
    /// Counts the sealed rows on <paramref name="owner" />'s worn items again, as a stats refresh that recounted Resonance
    /// found its gear, and keeps the slots they are in. With none, the pending fraction no worn item can take is dropped;
    /// with any, the next flush looks for a full bar, so a seal worn again with a full bar, or at login, wakes.
    /// </summary>
    internal void Recount(CharacterEntity owner)
    {
        int sealedWorn = FindSealedSlots(owner);
        SealedWorn = sealedWorn;
        if (sealedWorn == 0)
            PendingMilli = 0;
        else
            Due = true;
    }

    /// <summary>
    /// The Equipment slots of <paramref name="owner" /> holding an item with a seal that fills, in slot order: the ones
    /// the last recount found, or found again first when the Equipment changed since. No allocation once warm.
    /// </summary>
    internal ReadOnlySpan<byte> SealedSlots(CharacterEntity owner)
    {
        if (_slotsStale)
            FindSealedSlots(owner);

        return _sealedSlots.AsSpan(0, _sealedSlotCount);
    }

    /// <summary>The Equipment is about to change: the slots kept are found again at their next use.</summary>
    internal void EquipmentChanged() => _slotsStale = true;

    /// <summary>The whole points pending, taken out: the fraction stays.</summary>
    internal long TakeWholePoints()
    {
        long whole = PendingMilli / 1000;
        PendingMilli %= 1000;
        return whole;
    }

    /// <summary>
    /// The character is in the world through <paramref name="connection" />: from now on the meter puts it on
    /// <paramref name="queue" /> when it has work, and at once when it already has (a recount at the select).
    /// </summary>
    internal void Attach(AwakeningQueue queue, CharacterEntity owner, IWorldConnection connection)
    {
        _queue = queue;
        _owner = owner;
        Connection = connection;
        QueueIfPending();
    }

    /// <summary>The character left the world: it is queued no more, and an entry already queued is passed over.</summary>
    internal void Detach()
    {
        _queue = null;
        _owner = null;
        Connection = null;
    }

    /// <summary>The flush took the character off the queue: work found from now on queues it again.</summary>
    internal void Dequeued() => _queued = false;

    /// <summary>Puts the character on its queue when it has work, is in the world and is not queued already.</summary>
    internal void QueueIfPending()
    {
        if (_queued || _queue is null || !(_due || _unsent))
            return;

        _queued = true;
        _queue.Add(_owner!);
    }

    /// <summary>Keeps the Equipment slots holding a seal that fills, in slot order; answers how many such rows there are.</summary>
    private int FindSealedSlots(CharacterEntity owner)
    {
        CharacterInventoryContainer equipment = owner.Container(InventoryType.Equipment);
        int rowsFound = 0, slots = 0;
        for (byte slot = 0; slot < equipment.Capacity; slot++)
        {
            if (!equipment.TryGet(slot, out InventoryItem item))
                continue;

            IReadOnlyList<ItemInstanceModifier> rows = owner.ItemModifiers.Of(item.InstanceId);
            int found = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (Awakening.Fills(item, rows[i]))
                    found++;
            }

            if (found == 0)
                continue;

            if (_sealedSlots.Length < equipment.Capacity)
                Array.Resize(ref _sealedSlots, equipment.Capacity);

            _sealedSlots[slots++] = slot;
            rowsFound += found;
        }

        _sealedSlotCount = slots;
        _slotsStale = false;
        return rowsFound;
    }
}
