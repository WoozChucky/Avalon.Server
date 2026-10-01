using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Parties;
using Avalon.World.Public.Characters;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Characters;

/// <summary>
/// Gives a character experience already worked out by its caller (a kill's band-scaled share, a quest's reward,
/// #433) and levels it up past every requirement the total covers, carrying the rest. Each new level refreshes the
/// stats (#434): a living character is refilled to the new maximums, a dead one keeps its share of each pool, so a
/// level-up never revives a corpse. The party roster shows levels, so it is resent. A level with no requirement row
/// awards nothing and logs a warning. Tick thread only.
/// </summary>
public static class ExperienceAward
{
    public static void Grant(ICharacter character, uint experience, StaticData data, PartyService? parties, ILogger logger)
    {
        CharacterLevelExperience? requirement = data.CharacterLevelExperiences.FirstOrDefault(e => e.Level == character.Level);
        if (requirement is null)
        {
            logger.LogWarning("Experience requirement for level {Level} not found", character.Level);
            return;
        }

        ulong total = character.Experience + experience;
        bool levelled = false;

        // Level strictly rises and the rows are finite, so this ends: at the first level with no row, or the first
        // requirement the total does not reach.
        while (requirement is not null && total >= requirement.Experience)
        {
            total -= requirement.Experience;
            character.Level++;
            levelled = true;
            requirement = data.CharacterLevelExperiences.FirstOrDefault(e => e.Level == character.Level);
        }

        character.Experience = total;
        if (!levelled)
            return;

        character.RequiredExperience = requirement?.Experience ?? 0;

        if (character is CharacterEntity entity
            && !CharacterStatsRefresh.Apply(entity, data, entity.IsDead ? CurrentValues.KeepShare : CurrentValues.Refill))
        {
            logger.LogWarning("No class stats for {Class} level {Level}; {Name} keeps its old maximums",
                entity.Class, entity.Level, entity.Name);
        }

        parties?.LevelChanged(character);
    }
}
