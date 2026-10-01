using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public;
using Avalon.World.Public.Enums;

namespace Avalon.World.Quests;

/// <summary>
/// What a character's quests owe its client at the end of the tick (#433). WorldServer calls this once per tick
/// per connection, after both passes and <b>before</b> InventoryUpdateFlusher, which clears the slot changes this reads.
/// </summary>
public static class QuestFlusher
{
    public static void Flush(IWorldConnection connection, QuestService quests)
    {
        if (connection.Character is not CharacterEntity character)
            return;

        // A quest item that arrived, left or moved by any path this tick is counted again.
        if (TouchedTheBag(character.ClientChanges))
            quests.RecountCollect(character);
    }

    private static bool TouchedTheBag(InventoryClientChanges changes)
    {
        // An idle connection, most of them on most ticks: no enumerator, nothing allocated.
        if (changes.Slots.Count == 0)
            return false;

        foreach ((InventoryType container, ushort _) in changes.Slots)
        {
            if (container == InventoryType.Bag)
                return true;
        }

        return false;
    }
}
