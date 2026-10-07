using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Avalon.Api.Templates;

/// <summary>
/// Checks an edited item against the vendor stock rows and quests that use it, with the rules the world's Vendors and
/// Quests reloads apply (<see cref="ItemUseRules"/>, shared with the world's catalogs). Without it an edit the Items
/// reload reports applied could make the world refuse a vendor row or a quest at its next reload or start.
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
        if (!flags && !prices)
            return;

        var before = (ItemTemplate)entry.OriginalValues.ToObject();
        ItemTemplateId id = edited.Id;

        foreach (VendorStock row in await db.VendorStocks.AsNoTracking().Where(v => v.ItemTemplateId == id)
                     .OrderBy(v => v.Id).ToListAsync(ct))
        {
            string? problem = ItemUseRules.VendorStockProblem(edited, row.PriceOverride);
            if (problem is null || ItemUseRules.VendorStockProblem(before, row.PriceOverride) is not null)
                continue;
            string field = edited.Flags.HasFlag(ItemTemplateFlags.QuestItem) ? "flags" : "sellPrice";
            errors.Add(field, $"Vendor stock row {row.Id} of creature template {row.CreatureTemplateId.Value} would be refused: {problem}.");
        }

        if (!flags)
            return;

        foreach (QuestObjective objective in await db.QuestObjectives.AsNoTracking()
                     .Where(o => o.ItemTemplateId == id && o.Type == QuestObjectiveType.Collect)
                     .OrderBy(o => o.Id).ToListAsync(ct))
        {
            string? problem = ItemUseRules.CollectObjectiveProblem(edited, objective.Id);
            if (problem is not null && ItemUseRules.CollectObjectiveProblem(before, objective.Id) is null)
                errors.Add("flags", $"Quest {objective.QuestId.Value} would be refused: {problem}.");
        }

        foreach (QuestItemReward reward in await db.QuestItemRewards.AsNoTracking().Where(r => r.ItemTemplateId == id)
                     .OrderBy(r => r.QuestId).ToListAsync(ct))
        {
            string? problem = ItemUseRules.QuestRewardProblem(edited);
            if (problem is not null && ItemUseRules.QuestRewardProblem(before) is null)
                errors.Add("flags", $"Quest {reward.QuestId.Value} would be refused: {problem}.");
        }
    }
}
