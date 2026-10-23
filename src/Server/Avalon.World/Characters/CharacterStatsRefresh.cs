using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Public.Characters;
using Avalon.World.Reload;
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
    // The worn templates of the refresh running on this thread (the tick's, or a test's).
#pragma warning disable IDE1006
    [ThreadStatic] private static List<ItemTemplate>? t_worn;
#pragma warning restore IDE1006

    /// <summary>
    /// False, changing nothing, when there is no ClassLevelStat row for the character's class and
    /// level, or no ClassStatFactors row for its class (#506). A worn item counts only when its template exists and the slot it is in accepts it, so
    /// a row that put a gem in slot 8 or anything in 11-13 adds nothing. <paramref name="formula" /> bounds
    /// the speeds (#1178) and movement speed (#627), and is required: the caller passes the generation it read.
    /// </summary>
    public static bool Apply(
        CharacterEntity character,
        IReadOnlyCollection<ClassLevelStat> classStats,
        IReadOnlyDictionary<CharacterClass, ClassStatFactors> classFactors,
        Func<ItemTemplateId, ItemTemplate?> findTemplate,
        CurrentValues current,
        CombatFormula formula) =>
        Apply(character, classStats, classFactors, new Templates(findTemplate, null), current, formula);

    /// <summary>
    /// The refresh itself. The class row is the first of the character's class and level, and the worn items are read
    /// in the Equipment container's own order, as they always were; neither is found through a query any more (#883):
    /// the row lookup was a closure, a delegate and an enumerator per refresh, and so was each worn item's template
    /// over the current reference data, which walked every item template for it besides. The list of worn templates
    /// stays: it is what the calculator, shared with the simulator and the API, is handed. It is the calling thread's one
    /// list (#1178), emptied after each refresh, so a refresh allocates nothing for it.
    /// </summary>
    private static bool Apply(
        CharacterEntity character,
        IReadOnlyCollection<ClassLevelStat> classStats,
        IReadOnlyDictionary<CharacterClass, ClassStatFactors> classFactors,
        Templates templates,
        CurrentValues current,
        CombatFormula formula)
    {
        ClassLevelStat? row = RowOf(classStats, character.Class, character.Level);
        if (row is null || !classFactors.TryGetValue(character.Class, out ClassStatFactors? factors))
            return false;

        List<ItemTemplate> worn = t_worn ??= [];
        try
        {
            foreach (InventoryItem item in character.Container(InventoryType.Equipment).Held)
            {
                if (templates.Find(item.TemplateId) is { } template && EquipmentSlots.Accepts(item.Slot, template.Slot))
                    worn.Add(template);
            }

            // Auras: whatever the character holds now is folded in, so every refresh (select, gear, level-up, an aura)
            // agrees with every other.
            character.ApplyStats(CharacterStatsCalculator.Calculate(row, worn, factors, formula, character.Auras.StatTotals),
                current, formula);
        }
        finally
        {
            worn.Clear();
        }

        return true;
    }

    /// <summary>The same, over the current generation of reference data.</summary>
    public static bool Apply(CharacterEntity character, StaticData data, CurrentValues current)
    {
        // The combat area is read once, so the factors and the formula are one generation.
        CombatPatch combat = data.Combat;
        return Apply(character, data.ClassLevelStats, combat.Factors, new Templates(null, data), current, combat.Formula);
    }

    /// <summary>The first row of that class and level, in the rows' order, or null.</summary>
    private static ClassLevelStat? RowOf(IReadOnlyCollection<ClassLevelStat> classStats, CharacterClass characterClass, ushort level)
    {
        // By index where the rows allow it (the reference data's do): a foreach over the interface boxes an enumerator.
        if (classStats is IReadOnlyList<ClassLevelStat> rows)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Class == characterClass && rows[i].Level == level)
                    return rows[i];
            }

            return null;
        }

        foreach (ClassLevelStat row in classStats)
        {
            if (row.Class == characterClass && row.Level == level)
                return row;
        }

        return null;
    }

    /// <summary>
    /// Where a worn item's template is found: the caller's lookup, or the current reference data by id (its keyed
    /// lookup, the first template of an id as the walk it replaces answered). A value, so neither costs a delegate.
    /// </summary>
    private readonly struct Templates(Func<ItemTemplateId, ItemTemplate?>? find, StaticData? data)
    {
        public ItemTemplate? Find(ItemTemplateId id) => data is not null ? data.FindItemTemplate(id) : find!(id);
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
