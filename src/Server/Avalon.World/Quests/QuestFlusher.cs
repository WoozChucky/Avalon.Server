using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public;
using Avalon.World.Public.Enums;

namespace Avalon.World.Quests;

/// <summary>
/// What a character's quests owe its client at the end of the tick (#433). WorldServer calls this once per tick
/// per connection, after both passes and <b>before</b> InventoryUpdateFlusher, which clears the slot changes this reads.
/// Each step is contained on its own (final review M2): one that throws is logged at Error, throttled per step
/// (<see cref="QuestService.FlushStepFailed" />), and the steps after it still run, so a recount that throws on every
/// tick does not hold back the character's log, updates, lines or markers.
/// </summary>
public static class QuestFlusher
{
    public const string RecountStep = "Quest flush: collect recount";
    public const string EnterInstanceStep = "Quest flush: enter-instance hooks";
    public const string ClientStep = "Quest flush: log, updates and lines";
    public const string MarkersStep = "Quest flush: markers";

    public static void Flush(IWorldConnection connection, QuestService quests)
    {
        if (connection.Character is not CharacterEntity character)
            return;

        // A quest item that arrived, left or moved by any path this tick is counted again.
        if (TouchedTheBag(character.ClientChanges))
        {
            try
            {
                quests.RecountCollect(character);
            }
            catch (Exception e)
            {
                quests.FlushStepFailed(RecountStep, e);
            }
        }

        // The OnEnterInstance script hooks, once per instance (a login, a portal, a respawn).
        try
        {
            quests.EnteredInstanceIfChanged(character);
        }
        catch (Exception e)
        {
            quests.FlushStepFailed(EnterInstanceStep, e);
        }

        // The log the first time, then each changed quest once; then the lines; then the markers, whose inputs
        // (instance, level, log, catalog) the steps above may just have changed.
        try
        {
            quests.FlushClient(connection, character);
        }
        catch (Exception e)
        {
            quests.FlushStepFailed(ClientStep, e);
        }

        try
        {
            quests.FlushMarkers(connection, character);
        }
        catch (Exception e)
        {
            quests.FlushStepFailed(MarkersStep, e);
        }
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
