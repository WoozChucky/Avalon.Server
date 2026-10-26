using Avalon.Common;
using Avalon.Domain.World;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Public.Characters;

namespace Avalon.World.Items;

/// <summary>
/// The rules of a sealed affix's bar (#1180): which seals fill, which are ready to wake, and the settle that writes a
/// character's pending points into its worn rows. "Worn" is the Equipment container, here, in the meter's count and in
/// the wake. Tick thread only.
/// </summary>
public static class Awakening
{
    /// <summary>
    /// Whether <paramref name="row" /> of <paramref name="item" /> fills from kills and damage: a seal on an identified
    /// copy. Every count, settle, wake and progress packet asks this, so it is the one place a seal that must never fill
    /// or wake is told apart. An unidentified copy (the equip rule keeps one out of the Equipment, should one ever be
    /// there) neither fills nor tells its owner that it is sealed.
    /// </summary>
    public static bool Fills(in InventoryItem item, ItemInstanceModifier row) => item.Identified && row.Sealed;

    /// <summary>Whether <paramref name="row" /> fills and its bar reached the threshold of <paramref name="item" />'s item level.</summary>
    public static bool ReadyToWake(in InventoryItem item, ItemInstanceModifier row) =>
        Fills(item, row) && row.SealProgress >= ItemAwakening.Threshold(item.ItemLevel);

    /// <summary>
    /// The index in <paramref name="rows" /> (<paramref name="item" />'s modifier list) of the first row ready to wake
    /// (<see cref="ReadyToWake" />), or -1 for none: the one scan the flush's look for a full bar and the wake itself share.
    /// </summary>
    public static int ReadyRowIndex(in InventoryItem item, IReadOnlyList<ItemInstanceModifier> rows)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (ReadyToWake(item, rows[i]))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Moves <paramref name="character" />'s whole pending points into every row <see cref="Fills" /> accepts on its worn
    /// items (only those in the slots its meter keeps for its seals are visited), in place on the store's row, each capped at its item's threshold; the fraction stays pending. Each item whose
    /// progress moved is marked for the next save, and the meter is left due (the flush looks for a full bar) and unsent
    /// (the client is told the progress). It records no client slot and never wakes anything: only the tick's flush
    /// wakes. Answers whether any progress moved. Nothing to do, and nothing allocated, under a whole point.
    /// </summary>
    internal static bool Settle(CharacterEntity character)
    {
        AwakeningMeter meter = character.Awakening;
        if (meter.PendingMilli < 1000)
            return false;

        long whole = meter.TakeWholePoints();
        bool moved = false;
        CharacterInventoryContainer equipment = character.Container(InventoryType.Equipment);
        ReadOnlySpan<byte> slots = meter.SealedSlots(character);
        for (int s = 0; s < slots.Length; s++)
        {
            if (!equipment.TryGet(slots[s], out InventoryItem item))
                continue;

            IReadOnlyList<ItemInstanceModifier> rows = character.ItemModifiers.Of(item.InstanceId);
            bool itemMoved = false;
            for (int i = 0; i < rows.Count; i++)
            {
                ItemInstanceModifier row = rows[i];
                if (!Fills(item, row))
                    continue;

                int progress = row.SealProgress.GetValueOrDefault();
                int threshold = ItemAwakening.Threshold(item.ItemLevel);
                if (progress >= threshold)
                    continue;

                row.SealProgress = (int)Math.Min(progress + whole, threshold);
                itemMoved = true;
            }

            if (itemMoved)
            {
                character.SaveState.ItemModifiersChanged(item.InstanceId);
                moved = true;
            }
        }

        if (moved)
        {
            meter.Due = true;
            meter.Unsent = true;
        }

        return moved;
    }
}
