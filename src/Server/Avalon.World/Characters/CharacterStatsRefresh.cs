using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Characters;

/// <summary>
/// What happens to the current health and power pools when the maximums change. Fury (#526) is the one
/// pool that is not filled up: Refill and KeepShare keep its value, capped at the new maximum, and
/// EnterWorld empties it.
/// </summary>
public enum CurrentValues
{
    /// <summary>Health and a Mana or Energy pool are filled to the new maximum: a level-up.</summary>
    Refill,

    /// <summary>Health and a Mana or Energy pool keep their share of the new maximum: a gear change.</summary>
    KeepShare,

    /// <summary>Select: health and a Mana or Energy pool are filled, and Fury starts empty (#526).</summary>
    EnterWorld,
}

/// <summary>
/// Recalculates a character's stats from its class, level and what it is wearing, and writes them
/// (spec #463, #434). Tick thread only.
/// </summary>
public static class CharacterStatsRefresh
{
    /// <summary>
    /// False, changing nothing, when there is no ClassLevelStat row for the character's class and
    /// level, or no ClassStatFactors row for its class (#506). A worn item counts only when its template exists and the slot it is in accepts it, so
    /// a row that put a gem in slot 8 or anything in 11-13 adds nothing.
    /// </summary>
    public static bool Apply(
        CharacterEntity character,
        IReadOnlyCollection<ClassLevelStat> classStats,
        IReadOnlyDictionary<CharacterClass, ClassStatFactors> classFactors,
        Func<ItemTemplateId, ItemTemplate?> findTemplate,
        CurrentValues current)
    {
        ClassLevelStat? row = classStats.FirstOrDefault(s => s.Class == character.Class && s.Level == character.Level);
        if (row is null || !classFactors.TryGetValue(character.Class, out ClassStatFactors? factors))
            return false;

        List<ItemTemplate> worn = [];
        foreach (InventoryItem item in character.Container(InventoryType.Equipment).Items)
        {
            if (findTemplate(item.TemplateId) is { } template && EquipmentSlots.Accepts(item.Slot, template.Slot))
                worn.Add(template);
        }

        character.ApplyStats(CharacterStatsCalculator.Calculate(row, worn, factors), current);
        return true;
    }

    /// <summary>The same, over the current generation of reference data.</summary>
    public static bool Apply(CharacterEntity character, StaticData data, CurrentValues current)
    {
        IReadOnlyCollection<ItemTemplate> templates = data.ItemTemplates;
        return Apply(character, data.ClassLevelStats, data.Combat.Factors, id => templates.FirstOrDefault(t => t.Id == id),
            current);
    }

    /// <summary>
    /// After an accepted change to the Equipment container: KeepShare, so each pool keeps its
    /// share. Never throws: the change is already applied, and a throw here must not turn it into a
    /// refusal the client would believe.
    /// </summary>
    public static void AfterGearChange(CharacterEntity character, StaticData data, ILogger logger)
    {
        try
        {
            if (!Apply(character, data, CurrentValues.KeepShare))
            {
                logger.LogWarning("No class stats for {Class} level {Level}; {Name}'s gear change left its stats as they were",
                    character.Class, character.Level, character.Name);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Recalculating {Name}'s stats after a gear change threw", character.Name);
        }
    }
}
