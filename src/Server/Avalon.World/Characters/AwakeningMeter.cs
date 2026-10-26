using Avalon.Common;
using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Items;
using Avalon.World.Public.Characters;

namespace Avalon.World.Characters;

/// <summary>
/// One character's Awakening meter (#1180): the points its kills and damage earned for the sealed affixes it wears and
/// has not yet written into them. A hit adds to <see cref="PendingMilli" /> and writes nothing else: no row, no save
/// mark, no client change. <see cref="Awakening.Settle" /> moves its whole points into the worn rows. Made once with the
/// character; World-side, never on ICharacter, and never saved, so a fraction of a point left at a logout is lost. Tick
/// thread only.
/// </summary>
public sealed class AwakeningMeter
{
    /// <summary>
    /// How many sealed rows <see cref="Awakening.Fills" /> accepts on the worn items, as the last stats refresh that
    /// counted Resonance found them: 0 for a character with none, which makes a hit's meter one compare.
    /// </summary>
    public int SealedWorn { get; private set; }

    /// <summary>
    /// The points earned and not yet settled, in thousandths of a point. A hit's product is under 4.3e16 (a health of at
    /// most <see cref="uint.MaxValue" /> times a weight of at most
    /// <see cref="Configuration.AwakeningConfiguration.MaxWeight" /> times 1000), and a hit removes no more than its
    /// creature's health, so it adds about its weight in points at most: neither overflows 64 bits.
    /// </summary>
    public long PendingMilli { get; private set; }

    /// <summary>Whether the tick's Awakening flush must settle and look for a full bar: set by a kill, a settle that moved progress, or a recount that found a seal.</summary>
    public bool Due { get; internal set; }

    /// <summary>Whether progress moved since the client was last told it.</summary>
    public bool Unsent { get; internal set; }

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
    /// found its gear. With none, the pending fraction no worn item can take is dropped; with any, the next flush looks
    /// for a full bar, so a seal worn again with a full bar, or at login, wakes.
    /// </summary>
    internal void Recount(CharacterEntity owner)
    {
        int sealedWorn = 0;
        foreach (InventoryItem item in owner.Container(InventoryType.Equipment).Held)
        {
            IReadOnlyList<ItemInstanceModifier> rows = owner.ItemModifiers.Of(item.InstanceId);
            for (int i = 0; i < rows.Count; i++)
            {
                if (Awakening.Fills(item, rows[i]))
                    sealedWorn++;
            }
        }

        SealedWorn = sealedWorn;
        if (sealedWorn == 0)
            PendingMilli = 0;
        else
            Due = true;
    }

    /// <summary>The whole points pending, taken out: the fraction stays.</summary>
    internal long TakeWholePoints()
    {
        long whole = PendingMilli / 1000;
        PendingMilli %= 1000;
        return whole;
    }
}
