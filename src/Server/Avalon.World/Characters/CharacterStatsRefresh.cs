using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Items;
using Avalon.World.Public.Characters;
using Avalon.World.Reload;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Characters;

/// <summary>
/// What happens to the current health and mana when the maximums change (#1181: every class's pool is mana, treated as
/// health is).
/// </summary>
public enum CurrentValues
{
    /// <summary>Health and mana are filled to the new maximum: a level-up.</summary>
    Refill,

    /// <summary>Health and mana keep their share of the new maximum: a gear change.</summary>
    KeepShare,

    /// <summary>Select: health and mana are filled.</summary>
    EnterWorld,
}

/// <summary>
/// Recalculates a character's stats from its class, level and what it is wearing, and writes them
/// (spec #463, #434). Tick thread only.
/// </summary>
public static class CharacterStatsRefresh
{
    // The worn items of the refresh running on this thread (the tick's, or a test's).
#pragma warning disable IDE1006
    [ThreadStatic] private static List<WornItem>? t_worn;
#pragma warning restore IDE1006

    /// <summary>
    /// False, changing nothing, when there is no ClassLevelStat row for the character's class and level. A worn item counts
    /// only when its template exists and the slot it is in accepts it, so a row that put a gem in slot 8 or anything in the reserved slot 13 adds nothing. Each counts the
    /// template the lookup holds now, and the base stats and modifiers its copy rolled (#1179), so a template edited live
    /// reaches a worn copy's template lines and leaves its rolled base stats and modifiers as they were.
    /// <paramref name="formula" /> bounds the speeds (#1178) and movement speed (#627), and is required: the caller passes
    /// the generation it read. So is <paramref name="resonance" />, the table the character's Resonance counts are bound to
    /// and counted against (#1180): <see cref="ResonanceTable.Empty" /> for a caller without a catalog; and
    /// <paramref name="effects" />, the effect catalog its active Resonance effects are built from (#1180 part A2):
    /// <see cref="ResonanceEffectCatalog.Empty" /> for a caller without one.
    /// </summary>
    public static bool Apply(
        CharacterEntity character,
        IReadOnlyCollection<ClassLevelStat> classStats,
        Func<ItemTemplateId, ItemTemplate?> findTemplate,
        ResonanceTable resonance,
        ResonanceEffectCatalog effects,
        CurrentValues current,
        CombatFormula formula) =>
        Apply(character, classStats, new Templates(findTemplate, null), resonance, effects, current, formula);

    /// <summary>
    /// The refresh itself. The class row is the first of the character's class and level, and the worn items are read
    /// in the Equipment container's own order, as they always were; neither is found through a query any more (#883):
    /// the row lookup was a closure, a delegate and an enumerator per refresh, and so was each worn item's template
    /// over the current reference data, which walked every item template for it besides. The list of worn items
    /// stays: it is what the calculator, shared with the simulator and the API, is handed. It is the calling thread's one
    /// list (#1178), emptied after each refresh, so a refresh allocates nothing for it; each item's modifiers are the
    /// character's store's own list (#1179), null for an item without any, and its base stats a value read from the
    /// same entry, so they allocate nothing either. The worn items' Resonance counts (#1180) are the character's own
    /// buffer, bound to <paramref name="resonance" /> (reallocated only when a reload changed the number of themes) and
    /// filled in the calculation's one walk, only when the table or a worn item's modifier list changed since they were
    /// last counted (<see cref="ResonanceInputs" />); a refresh over the same gear and table keeps them. The active
    /// Resonance effects (#1180 part A2) are rebuilt from the counts, into the character's own reused slots
    /// (<see cref="ResonanceProcs" />), only when the counts were counted again or <paramref name="effects" /> is not the
    /// catalog they were built from; a refresh that did neither keeps them.
    /// </summary>
    private static bool Apply(
        CharacterEntity character,
        IReadOnlyCollection<ClassLevelStat> classStats,
        Templates templates,
        ResonanceTable resonance,
        ResonanceEffectCatalog effects,
        CurrentValues current,
        CombatFormula formula)
    {
        ClassLevelStat? row = RowOf(classStats, character.Class, character.Level);
        if (row is null)
            return false;

        List<WornItem> worn = t_worn ??= [];
        try
        {
            foreach (InventoryItem item in character.Container(InventoryType.Equipment).Held)
            {
                if (templates.Find(item.TemplateId) is { } template && EquipmentSlots.Accepts(item.Slot, template.Slot))
                {
                    ItemTemplate? named = template.BaseTemplateId is { } baseId ? templates.Find(baseId) : null;
                    worn.Add(character.ItemModifiers.WornOf(template, WornItem.DamageTypeOf(template, named), item.InstanceId));
                }
            }

            // Resonance is counted again only when the table or a worn modifier list moved since the last count; a
            // refresh that moved neither (an aura, a level-up) keeps the counts it has.
            character.Resonance.Use(resonance);
            ResonanceInputs counted = character.ResonanceCountedFrom;
            bool recount = !counted.Match(resonance, worn);

            // Auras: whatever the character holds now is folded in, so every refresh (select, gear, level-up, an aura)
            // agrees with every other.
            DerivedCharacterStats stats = CharacterStatsCalculator.Calculate(row, worn, formula,
                character.Auras.StatTotals, recount ? character.Resonance : null);
            if (recount)
            {
                counted.Remember(resonance, worn);

                // #1180: the worn seals are counted with the gear they are on, so a hit reads one count.
                character.Awakening.Recount(character);
            }

            character.ApplyStats(stats, current, formula);

            // The active effects follow the counts: rebuilt when they were counted again or the effect catalog moved.
            ResonanceProcs procs = character.ResonanceProcs;
            if (recount || !ReferenceEquals(procs.BuiltFrom, effects))
                procs.Rebuild(character.Resonance, effects);
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
        // The combat and items areas are each read once, so the formula is one generation, and so are the Resonance table
        // and the effects keyed by its themes.
        CombatPatch combat = data.Combat;
        ItemsPatch items = data.Items;
        return Apply(character, data.ClassLevelStats, new Templates(null, data), items.Modifiers.Resonance,
            items.Effects, current, combat.Formula);
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
