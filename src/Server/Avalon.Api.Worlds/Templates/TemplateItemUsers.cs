using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Api.Worlds.Templates;

/// <summary>
/// Checks an edited item against the rows that use it and the rows it uses, with the rules the world applies to them.
/// The vendor stock rows and quests that use it, by the rules of the world's Vendors and Quests reloads
/// (<see cref="ItemUseRules"/>, shared with the world's catalogs). And by the Items reload's own rules (#1179): a rarity
/// or base-kind change against the modifiers the template links (<see cref="ItemModifierCatalog.RarityRefusal"/>), a
/// unique against the base it names, and a base against the uniques made on it
/// (<see cref="ItemBaseStatRules.NamedBaseProblem"/>). Without it an edit the Items reload reports applied could make the
/// world refuse a vendor row or a quest at its next reload or start, or leave the item, or a unique made on it, out.
/// Of the rows that use it, only a problem the edit causes is reported: a row that was already refused before it is not
/// this edit's doing. The edited template itself is judged as it would be stored, as the world judges it.
/// </summary>
public static class TemplateItemUsers
{
    public static async Task ValidateAsync(WorldDbContext db, ItemTemplate edited, TemplateErrors errors, CancellationToken ct)
    {
        db.ChangeTracker.DetectChanges();
        EntityEntry<ItemTemplate> entry = db.Entry(edited);
        bool flags = entry.Property(i => i.Flags).IsModified;
        bool prices = entry.Property(i => i.BuyPrice).IsModified || entry.Property(i => i.SellPrice).IsModified;
        bool rarity = entry.Property(i => i.Rarity).IsModified;
        var before = (ItemTemplate)entry.OriginalValues.ToObject();

        await UsersAsync(db, edited, before, flags, prices, rarity, errors, ct);
        await BaseRulesAsync(db, entry, before, errors, ct);
    }

    /// <summary>The vendor stock rows and quests that use the item, against a change of its flags, prices or rarity.</summary>
    private static async Task UsersAsync(WorldDbContext db, ItemTemplate edited, ItemTemplate before, bool flags, bool prices,
        bool rarity, TemplateErrors errors, CancellationToken ct)
    {
        if (!flags && !prices && !rarity)
            return;

        ItemTemplateId id = edited.Id;

        foreach (VendorStock row in await db.VendorStocks.AsNoTracking().Where(v => v.ItemTemplateId == id)
                     .OrderBy(v => v.Id).ToListAsync(ct))
        {
            string? problem = ItemUseRules.VendorStockProblem(edited, row.PriceOverride);
            if (problem is null || ItemUseRules.VendorStockProblem(before, row.PriceOverride) is not null)
                continue;
            string field = edited.Flags.HasFlag(ItemTemplateFlags.QuestItem) ? "flags"
                : edited.Rarity != ItemRarity.Common ? "rarity"
                : "sellPrice";
            errors.Add(field, $"Vendor stock row {row.Id} of creature template {row.CreatureTemplateId.Value} would be refused: {problem}.");
        }

        if (!flags && !rarity)
            return;

        if (flags)
        {
            foreach (QuestObjective objective in await db.QuestObjectives.AsNoTracking()
                         .Where(o => o.ItemTemplateId == id && o.Type == QuestObjectiveType.Collect)
                         .OrderBy(o => o.Id).ToListAsync(ct))
            {
                string? problem = ItemUseRules.CollectObjectiveProblem(edited, objective.Id);
                if (problem is not null && ItemUseRules.CollectObjectiveProblem(before, objective.Id) is null)
                    errors.Add("flags", $"Quest {objective.QuestId.Value} would be refused: {problem}.");
            }
        }

        // A fixed reward and a choice are judged on the template alone (a Unique flag, or Unique rarity, #1179): an edit that
        // newly breaks neither reads no rows. The error names the field that broke it.
        string? rewardProblem = ItemUseRules.QuestRewardProblem(edited);
        if (rewardProblem is null || ItemUseRules.QuestRewardProblem(before) is not null)
            return;
        string rewardField = (edited.Flags & ItemTemplateFlags.Unique) != 0 ? "flags" : "rarity";

        foreach (QuestItemReward row in await db.QuestItemRewards.AsNoTracking().Where(r => r.ItemTemplateId == id)
                     .OrderBy(r => r.QuestId).ToListAsync(ct))
        {
            errors.Add(rewardField, $"Quest {row.QuestId.Value} would be refused: {rewardProblem}.");
        }

        foreach (QuestItemChoice row in await db.QuestItemChoices.AsNoTracking().Where(c => c.ItemTemplateId == id)
                     .OrderBy(c => c.QuestId).ToListAsync(ct))
        {
            errors.Add(rewardField, $"Quest {row.QuestId.Value} would be refused: {rewardProblem}.");
        }
    }

    /// <summary>
    /// The Items reload's rules that read other rows (#1179), so the world never leaves the edited template, or a unique
    /// made on it, out. A rarity or base-kind change holds the template's links to its rarity and kind (the narrow read
    /// of its links): a Common base links one implicit, a Common template without a kind none. A unique, as it would be
    /// stored, names a base the world keeps, a Common base of its kind at or under its level. And a base keeps every
    /// unique made on it so: a change of its rarity, kind or level that would leave one out is refused. Each error is keyed
    /// by the field the edit changed that broke the rule.
    /// </summary>
    private static async Task BaseRulesAsync(
        WorldDbContext db, EntityEntry<ItemTemplate> entry, ItemTemplate before, TemplateErrors errors, CancellationToken ct)
    {
        ItemTemplate edited = entry.Entity;
        ItemTemplateId id = edited.Id;
        bool rarity = entry.Property(i => i.Rarity).IsModified;
        bool kind = entry.Property(i => i.BaseKind).IsModified;
        bool level = entry.Property(i => i.RequiredLevel).IsModified;
        bool named = entry.Property(i => i.BaseTemplateId).IsModified;

        if (rarity || kind)
        {
            // As the world judges it (ItemsPatch.Build): a unique that links nothing is kept, so only its links are held.
            ItemModifierCatalog linked = await ItemModifierQueries.LinkedToAsync(db, id, NullLogger.Instance, ct);
            IReadOnlyList<ItemModifier> links = linked.LinkedTo(id);
            if ((edited.Rarity != ItemRarity.Unique || links.Count > 0)
                && ItemModifierCatalog.RarityRefusal(edited.Rarity, edited.BaseKind, links) is { } linkProblem)
            {
                errors.Add(kind ? "baseKind" : "rarity", $"Item template {id.Value} would be left out by the world: {linkProblem}.");
            }
        }

        ItemTemplate? itsBase = edited.BaseTemplateId is { } baseId && baseId != id ? await KeptAsync(db, baseId, ct) : null;
        if (ItemBaseStatRules.NamedBaseProblem(edited, itsBase) is { } baseProblem)
        {
            string field = named ? "baseTemplateId" : kind ? "baseKind" : level ? "requiredLevel" : rarity ? "rarity" : "baseTemplateId";
            errors.Add(field, $"Item template {id.Value} would be left out by the world: {baseProblem}.");
        }

        if (!rarity && !kind && !level)
            return;

        // By id in memory: SQLite, the tests' database, cannot order by an unsigned key.
        List<ItemTemplate> uniques = await db.ItemTemplates.AsNoTracking()
            .Where(t => t.BaseTemplateId == id && t.Id != id).ToListAsync(ct);
        foreach (ItemTemplate unique in uniques.OrderBy(t => t.Id.Value))
        {
            if (ItemBaseStatRules.NamedBaseProblem(unique, edited) is not { } problem
                || ItemBaseStatRules.NamedBaseProblem(unique, before) is not null)
            {
                continue;
            }

            string field = rarity && edited.Rarity != before.Rarity ? "rarity"
                : kind && edited.BaseKind != before.BaseKind ? "baseKind"
                : "requiredLevel";
            errors.Add(field, $"Unique item template {unique.Id.Value} '{unique.Name}' made on it would be left out by the world: {problem}.");
        }
    }

    /// <summary>
    /// The template of <paramref name="id" /> as stored, for a unique that names it as its base: null when there is none,
    /// or when the world would leave it out by its own rules (its links, its base stats, a base it names itself), since the
    /// world judges a unique's base only among the templates it keeps. A Unique template comes back as stored, unjudged:
    /// <see cref="ItemBaseStatRules.NamedBaseProblem" /> refuses it as a base whatever its own rows.
    /// </summary>
    private static async Task<ItemTemplate?> KeptAsync(WorldDbContext db, ItemTemplateId id, CancellationToken ct)
    {
        ItemTemplate? template = await db.ItemTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null || template.Rarity == ItemRarity.Unique)
            return template;

        ItemModifierCatalog linked = await ItemModifierQueries.LinkedToAsync(db, id, NullLogger.Instance, ct);
        bool kept = ItemModifierCatalog.RarityRefusal(template.Rarity, template.BaseKind, linked.LinkedTo(id)) is null
                    && ItemBaseStatRules.Problem(template) is null
                    && ItemBaseStatRules.NamedBaseProblem(template, null) is null;
        return kept ? template : null;
    }
}
