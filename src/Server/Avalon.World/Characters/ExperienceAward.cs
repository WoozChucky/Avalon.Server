using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Parties;
using Avalon.World.Public.Characters;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Characters;

/// <summary>
/// Gives a character experience already worked out by its caller (a kill's band-scaled share, a quest's reward,
/// #433) and levels it up past every requirement the total covers, carrying the rest. A character can gain experience
/// only while its level and the next both have a requirement row (<see cref="CanGainExperience"/>, the one rule the
/// award and the party split share; #735): so at the highest level with a row (the maximum level), and on a level
/// whose next level is missing (a gap, treated exactly like the maximum), it gains nothing, and an award that levels a
/// character onto such a level stops there, the rest discarded, so it arrives with none. Nothing is logged for either. Each new level refreshes the stats (#434): a living character is
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

        CharacterLevelExperience? next = Requirement(data, (ushort)(character.Level + 1));
        if (next is null)
            return;   // the maximum level, or the level before a gap: no experience, and nothing logged

        ulong total = character.Experience + experience;
        bool levelled = false;

        // Level strictly rises and the rows are finite, so this ends: at the first requirement the total does not
        // reach, or on arriving at a level whose next level has no row, where the rest of the award is discarded.
        while (total >= requirement.Experience)
        {
            total -= requirement.Experience;
            character.Level++;
            levelled = true;
            requirement = next;

            next = Requirement(data, (ushort)(character.Level + 1));
            if (next is null)
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
    /// Whether <paramref name="character"/> can still gain experience: its level and the next both have a requirement
    /// row. False at the maximum level (the highest row), on the level before a gap in the rows, and on a level with no
    /// row at all. The one rule <see cref="Grant"/> and the party split (<see cref="PartyExperience"/>) share (#735).
    /// </summary>
    public static bool CanGainExperience(ICharacter character, StaticData data) =>
        Requirement(data, character.Level) is not null
        && Requirement(data, (ushort)(character.Level + 1)) is not null;

    private static CharacterLevelExperience? Requirement(StaticData data, ushort level) =>
        data.CharacterLevelExperiences.FirstOrDefault(e => e.Level == level);
}
