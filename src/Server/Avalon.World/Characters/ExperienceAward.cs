using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Parties;
using Avalon.World.Public.Characters;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Characters;

/// <summary>
/// Gives a character experience already worked out by its caller (a kill's band-scaled share, a quest's reward,
/// #433) and levels it up past every requirement the total covers, carrying the rest. A level is entered only when it
/// has a requirement row (#735): at the last one the total is held at its threshold, so the maximum level is the
/// data's highest level, and nothing past it is ever reached or logged. Each new level refreshes the stats (#434): a
/// living character is refilled to the new maximums, a dead one keeps its share of each pool, so a level-up never
/// revives a corpse. The party roster shows levels, so it is resent. A character whose current level has no row (none
/// does today) is awarded nothing and a warning is logged. Copper and items never pass through
/// here, so they are paid at the cap as anywhere else. Tick thread only.
/// </summary>
public static class ExperienceAward
{
    public static void Grant(ICharacter character, uint experience, StaticData data, PartyService? parties, ILogger logger)
    {
        CharacterLevelExperience? requirement = Requirement(data, character.Level);
        if (requirement is null)
        {
            logger.LogWarning("Experience requirement for level {Level} not found", character.Level);
            return;
        }

        ulong total = character.Experience + experience;
        bool levelled = false;

        // Level strictly rises and the rows are finite, so this ends: at the first requirement the total does not
        // reach, or at the last level with a row, where the total stops at that level's threshold.
        while (total >= requirement.Experience)
        {
            CharacterLevelExperience? next = Requirement(data, (ushort)(character.Level + 1));
            if (next is null)
            {
                total = requirement.Experience;
                break;
            }

            total -= requirement.Experience;
            character.Level++;
            levelled = true;
            requirement = next;
        }

        character.Experience = total;
        if (!levelled)
            return;

        character.RequiredExperience = requirement.Experience;

        if (character is CharacterEntity entity
            && !CharacterStatsRefresh.Apply(entity, data, entity.IsDead ? CurrentValues.KeepShare : CurrentValues.Refill))
        {
            logger.LogWarning("No class stats for {Class} level {Level}; {Name} keeps its old maximums",
                entity.Class, entity.Level, entity.Name);
        }

        parties?.LevelChanged(character);
    }

    private static CharacterLevelExperience? Requirement(StaticData data, ushort level) =>
        data.CharacterLevelExperiences.FirstOrDefault(e => e.Level == level);
}
