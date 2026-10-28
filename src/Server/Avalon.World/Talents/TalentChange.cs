using Avalon.World.Characters;
using Avalon.World.Entities;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Talents;

/// <summary>
/// What follows a change of the nodes a character holds (#1181): an accepted allocation, and Part C's refund. On the tick.
/// </summary>
public static class TalentChange
{
    /// <summary>
    /// Sums the held nodes' lines again, rebuilds the held abilities from the tree (an ability already held keeps its
    /// timers, a new one arrives ready) and shapes each by the capstones held, refreshes the stats keeping each pool's
    /// share, marks the ability list owed only when the abilities held or the shape of one changed, and marks the talent
    /// window's state owed. Both follow at the tick's flush (<see cref="TalentFlusher" />), after the request's result, so
    /// any number of changes in a tick sends one list and one state, and a change allocates nothing once warm. The tree
    /// and the abilities are <paramref name="data" />'s, one generation. Both are owed even when a step throws: a rebuilt
    /// list reaches the client (its amounts from the stats as they stand at the flush) though the refresh failed, so the
    /// client never lacks an ability the server lets it cast, and the window still learns what is held.
    /// </summary>
    public static void Apply(CharacterEntity character, StaticData data, ILogger logger)
    {
        bool abilitiesChanged = false;
        try
        {
            TalentCatalog tree = data.Talents;
            character.Talents.RebuildTotals(tree);
            abilitiesChanged = TalentAbilities.Rebuild(character, tree, data.Abilities, logger);

            if (!CharacterStatsRefresh.Apply(character, data, CurrentValues.KeepShare))
            {
                logger.LogWarning(
                    "No class stats for {Class} level {Level}; character {CharacterId}'s talent change left its stats as they were",
                    character.Class, character.Level, character.Guid.Id);
            }
        }
        finally
        {
            if (abilitiesChanged)
                character.Talents.MarkAbilitiesOwed();
            character.Talents.MarkStateOwed();
        }
    }
}
