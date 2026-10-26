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
    /// Whether <paramref name="row" /> of <paramref name="item" /> fills from kills and damage: every count, settle and
    /// wake asks this, so it is the one place a seal that must never wake is told apart.
    /// </summary>
    public static bool Fills(in InventoryItem item, ItemInstanceModifier row) => row.Sealed;

    /// <summary>Whether <paramref name="row" /> fills and its bar reached the threshold of <paramref name="item" />'s item level.</summary>
    public static bool ReadyToWake(in InventoryItem item, ItemInstanceModifier row) =>
        Fills(item, row) && row.SealProgress >= ItemAwakening.Threshold(item.ItemLevel);

    /// <summary>
    /// Moves <paramref name="character" />'s whole pending points into every row <see cref="Fills" /> accepts on its worn
    /// items, in place on the store's row, each capped at its item's threshold; the fraction stays pending. Each item whose
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
        foreach (InventoryItem item in character.Container(InventoryType.Equipment).Held)
        {
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
