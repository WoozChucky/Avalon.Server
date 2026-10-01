using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Parties;
using Avalon.World.Public.Characters;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Characters;

/// <summary>
/// Gives a character experience already worked out by its caller (a kill's band-scaled share, a quest's reward,
/// #433) and levels it up past every requirement the total covers, carrying the rest. The maximum level is the highest
/// level with a requirement row (<see cref="MaxLevel"/>, the one source; #735): a character there gains no experience
/// at all, and an award that levels a character into it stops there, the rest discarded, so it enters the maximum level
/// with none. Nothing is logged for being at the cap. Each new level refreshes the stats (#434): a living character is
/// refilled to the new maximums, a dead one keeps its share of each pool, so a level-up never revives a corpse. The
/// party roster shows levels, so it is resent. A character whose current level has no row (none does today) is awarded
/// nothing and a warning is logged. Copper and items never pass through here, so they are paid at the cap as anywhere
/// else. Tick thread only.
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

        ushort maxLevel = MaxLevel(data) ?? requirement.Level;   // never null here: the current level has a row
        if (character.Level >= maxLevel)
            return;

        ulong total = character.Experience + experience;
        bool levelled = false;

        // Level strictly rises and stops at the maximum, so this ends: at the first requirement the total does not
        // reach, or on entering the maximum level, where the rest of the award is discarded.
        while (total >= requirement.Experience)
        {
            CharacterLevelExperience? next = Requirement(data, (ushort)(character.Level + 1));
            if (next is null)
            {
                // A gap in the rows below the maximum (none today): the next level cannot be entered, so the total
                // stops at this level's threshold.
                total = requirement.Experience;
                break;
            }

            total -= requirement.Experience;
            character.Level++;
            levelled = true;
            requirement = next;

            if (character.Level >= maxLevel)
            {
                total = 0;
                break;
            }
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

    /// <summary>
    /// The maximum level: the highest level with a requirement row, or null when there are none. The one definition the
    /// award and the party split (<see cref="PartyExperience"/>) share.
    /// </summary>
    public static ushort? MaxLevel(StaticData data)
    {
        ushort? max = null;
        foreach (CharacterLevelExperience row in data.CharacterLevelExperiences)
        {
            if (max is null || row.Level > max)
                max = row.Level;
        }

        return max;
    }

    private static CharacterLevelExperience? Requirement(StaticData data, ushort level) =>
        data.CharacterLevelExperiences.FirstOrDefault(e => e.Level == level);
}
