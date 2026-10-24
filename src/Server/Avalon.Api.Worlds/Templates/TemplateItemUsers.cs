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
/// Checks an edited item against the vendor stock rows and quests that use it, with the rules the world's Vendors and
/// Quests reloads apply (<see cref="ItemUseRules"/>, shared with the world's catalogs), and a rarity change against
/// the modifiers the template links (#1179, <see cref="ItemModifierCatalog.RarityRefusal"/>, which the Items reload
/// applies). Without it an edit the Items reload reports applied could make the world refuse a vendor row or a quest at
/// its next reload or start, or leave the item out.
/// Only a problem the edit causes is reported: a row that was already refused before it is not this edit's doing.
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
        if (!flags && !prices && !rarity)
            return;

        var before = (ItemTemplate)entry.OriginalValues.ToObject();
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

        // #1179: a rarity change holds the template's modifier links against the new rarity (the narrow read of its
        // links), so the world never leaves the template out at its next Items reload.
        if (rarity)
        {
            ItemModifierCatalog linked = await ItemModifierQueries.LinkedToAsync(db, id, NullLogger.Instance, ct);
            if (ItemModifierCatalog.RarityRefusal(edited.Rarity, linked.LinkedTo(id)) is { } linkProblem)
                errors.Add("rarity", $"Item template {id.Value} would be left out by the world: {linkProblem}.");
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
}
