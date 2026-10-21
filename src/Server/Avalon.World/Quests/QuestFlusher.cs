using Avalon.Common;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public;

namespace Avalon.World.Quests;

/// <summary>
/// What a character's quests owe its client at the end of the tick (#433). WorldServer calls this once per tick
/// per connection, after both passes and <b>before</b> InventoryUpdateFlusher, which clears the slot changes this reads.
/// Each step is contained on its own (final review M2): one that throws is logged at Error, throttled per step
/// (<see cref="QuestService.FlushStepFailed" />), and the steps after it still run, so a recount that throws on every
/// tick does not hold back the character's log, updates, lines or markers. The reset generation (#710) is an outside
/// input looked at like the catalog's: a daily, weekly or monthly reset that passed since the session last looked runs
/// <see cref="QuestService.ApplyResets" /> first, so the markers and doodad gates of a character it affects are worked
/// out again on the tick it passed, without a relog.
/// </summary>
public static class QuestFlusher
{
    public const string ResetStep = "Quest flush: reset passed";
    public const string ReloadStep = "Quest flush: settle after a reload";
    public const string RecountStep = "Quest flush: collect recount";
    public const string EnterInstanceStep = "Quest flush: enter-instance hooks";
    public const string ClientStep = "Quest flush: log, updates and lines";
    public const string MarkersStep = "Quest flush: markers";

    public static void Flush(IWorldConnection connection, QuestService quests)
    {
        if (connection.Character is not CharacterEntity character)
            return;

        // Nothing any step below reads changed since the last flush that left nothing over (#977): every connection
        // is flushed every tick, and almost every tick changes no one's quests. The character's own inputs mark the log
        // (QuestLog.FlushOwed); the catalog, which a reload swaps for everyone at once, the reset generation, which a
        // daily, weekly or monthly reset raises for everyone at once (#710), and the Bag's changes, which the inventory
        // keeps, are looked at here.
        QuestLog log = character.Quests;
        QuestCatalog catalog = quests.Catalog;
        bool resetPassed = log.ResetGeneration != quests.Resets.Generation;
        bool bagTouched = TouchedTheBag(character.ClientChanges);
        if (!log.FlushOwed && !resetPassed && !bagTouched && ReferenceEquals(log.SettledCatalog, catalog)
            && ReferenceEquals(log.MarkersCatalog, catalog))
        {
            return;
        }

        FlushSteps(connection, character, quests, resetPassed, bagTouched);

        // Kept for the next tick while a step still has something to do: the hooks and the markers wait for a live
        // instance (a character between instances), and a step that threw left its changes or lines unsent.
        log.FlushOwed = StillOwed(character, log, quests.Catalog);
    }

    private static void FlushSteps(IWorldConnection connection, CharacterEntity character, QuestService quests,
        bool resetPassed, bool bagTouched)
    {
        // A reset since the session last looked (#710): a character holding a completion it made available again gets
        // its markers and doodad gates worked out again; anyone else only notes the generation.
        if (resetPassed)
        {
            try
            {
                quests.ApplyResets(character);
            }
            catch (Exception e)
            {
                quests.FlushStepFailed(ResetStep, e);
            }
        }

        // A /reload quests since the last flush (#738): every held quest is settled once against the new catalog.
        try
        {
            quests.SettleAfterReload(character);
        }
        catch (Exception e)
        {
            quests.FlushStepFailed(ReloadStep, e);
        }

        // A quest item that arrived, left or moved by any path this tick is counted again.
        if (bagTouched)
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

    /// <summary>
    /// Whether a flush now would still do something, the catalog and the Bag aside: the log not sent yet, an update or a
    /// line not sent, the hooks or the markers not yet run for the character's instance, level, log and catalog.
    /// Each step's own test for having nothing to do.
    /// </summary>
    private static bool StillOwed(CharacterEntity character, QuestLog log, QuestCatalog catalog)
    {
        Guid instance = character.InstanceId;
        return !log.LogSent || log.ClientChanges.Count > 0 || log.PendingLines.Count > 0
               || log.ScriptsInstance != instance
               || log.MarkersInstance != instance || log.MarkersLevel != character.Level
               || log.MarkersVersion != log.MarkerVersion || !ReferenceEquals(log.MarkersCatalog, catalog);
    }

    private static bool TouchedTheBag(InventoryClientChanges changes)
    {
        // The changes hand out the set's own enumerator, a struct, so nothing is allocated, changed or not.
        foreach ((InventoryType container, ushort _) in changes)
        {
            if (container == InventoryType.Bag)
                return true;
        }

        return false;
    }
}
