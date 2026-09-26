using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;

namespace Avalon.World.Characters;

/// <summary>What happens to the current health and power pools when the maximums change.</summary>
public enum CurrentValues
{
    /// <summary>Both pools are filled to the new maximum: a level-up, and entering the world.</summary>
    Refill,

    /// <summary>Each pool keeps its share of the new maximum: a gear change.</summary>
    KeepShare,
}

/// <summary>
/// Recalculates a character's stats from its class, level and what it is wearing, and writes them
/// (spec #463, #434). Tick thread only.
/// </summary>
public static class CharacterStatsRefresh
{
    /// <summary>
    /// False, changing nothing, when there is no ClassLevelStat row for the character's class and
    /// level. A worn item counts only when its template exists and the slot it is in accepts it, so
    /// a row that put a gem in slot 8 or anything in 11-13 adds nothing.
    /// </summary>
    public static bool Apply(
        CharacterEntity character,
        IReadOnlyCollection<ClassLevelStat> classStats,
        Func<ItemTemplateId, ItemTemplate?> findTemplate,
        CurrentValues current)
    {
        ClassLevelStat? row = classStats.FirstOrDefault(s => s.Class == character.Class && s.Level == character.Level);
        if (row is null)
            return false;

        List<ItemTemplate> worn = [];
        foreach (InventoryItem item in character.Container(InventoryType.Equipment).Items)
        {
            if (findTemplate(item.TemplateId) is { } template && EquipmentSlots.Accepts(item.Slot, template.Slot))
                worn.Add(template);
        }

        character.ApplyStats(CharacterStatsCalculator.Calculate(row, worn), current);
        return true;
    }

    /// <summary>The same, over the current generation of reference data.</summary>
    public static bool Apply(CharacterEntity character, StaticData data, CurrentValues current)
    {
        IReadOnlyCollection<ItemTemplate> templates = data.ItemTemplates;
        return Apply(character, data.ClassLevelStats, id => templates.FirstOrDefault(t => t.Id == id), current);
    }
}
