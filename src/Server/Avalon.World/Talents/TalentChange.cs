using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Talents;

/// <summary>
/// What follows a change of the nodes a character holds (#1181): an accepted allocation, and Part C's refund. On the tick.
/// </summary>
public static class TalentChange
{
    /// <summary>
    /// Sums the held nodes' lines again, rebuilds the held abilities from the tree (an ability already held keeps its
    /// timers, a new one arrives ready), refreshes the stats keeping each pool's share, sends the ability list only when
    /// the abilities held changed, and marks the talent window's state owed, for the tick's flush. The tree and the
    /// abilities are <paramref name="data" />'s, one generation. The ability list and the owed state follow even when a
    /// step throws: a rebuilt list reaches the client (its amounts from the stats as they stand) though the refresh
    /// failed, so the client never lacks an ability the server lets it cast, and the window still learns what is held.
    /// </summary>
    public static void Apply(IWorldConnection connection, CharacterEntity character, StaticData data, ILogger logger)
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
            try
            {
                // Sent after the refresh, so each ability's amount and pace are worked out from the stats the tree now
                // gives.
                if (abilitiesChanged)
                    CharacterAbilitiesSender.Send(connection, character);
            }
            finally
            {
                character.Talents.MarkStateOwed();
            }
        }
    }
}
