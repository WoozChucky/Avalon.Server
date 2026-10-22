using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Quest;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public.Characters;

namespace Avalon.World.Quests;

/// <summary>
/// Whether a turn-in can go ahead, changing nothing (#433): ready; the last stage's Collect items all in the bag; the
/// pick one of the quest's choice set, or none for a quest without one (#711); every reward template, fixed and
/// picked, known and not Unique; the fixed rewards and the pick fit the bag as it will be once every quest item has
/// left it; the gold under the cap. Everything after it is ordered so nothing can refuse, as VendorRules is for a
/// trade.
/// </summary>
public static class QuestTurnInRules
{
    public static QuestResult Decide(CharacterEntity character, QuestView quest, ActiveQuest active, ulong choiceItemTemplateId,
        Func<ItemTemplateId, ItemTemplate?> findTemplate, ulong maxGold, out IReadOnlyList<(ItemTemplate Template, uint Count)> rewards)
    {
        List<(ItemTemplate Template, uint Count)> paid = [];
        rewards = paid;
        if (active.State != CharacterQuestState.ReadyToTurnIn)
            return QuestResult.NotReady;

        foreach (QuestObjectiveView objective in quest.LastStage.Objectives)
        {
            if (objective.Type == QuestObjectiveType.Collect && QuestService.HeldInBag(character, objective.ItemTemplateId!) < objective.Count)
                return QuestResult.NotReady;
        }

        // The pick names an item template, not a place in the set, so a reload that changed the set cannot turn it
        // into another item. Without a set, a pick is a client mistake, refused rather than ignored.
        QuestItemRewardView? choice = Choice(quest, choiceItemTemplateId);
        if (quest.ItemChoices.Count == 0 ? choiceItemTemplateId != 0 : choice is null)
            return QuestResult.InvalidChoice;

        foreach (QuestItemRewardView reward in quest.ItemRewards)
        {
            // A reload removed a reward's template after the catalog was built: nothing is paid rather than part.
            // The catalog refuses a Unique reward, but /reload items can make one Unique since: TryAdd could then
            // refuse it after the quest items left, so the turn-in is refused instead.
            if (findTemplate(reward.ItemTemplateId) is not { } template
                || (template.Flags & ItemTemplateFlags.Unique) != 0)
            {
                return QuestResult.Error;
            }

            paid.Add((template, reward.Count));
        }

        // The pick is one more reward item, judged as the fixed ones are and placed after them.
        if (choice is not null)
        {
            if (findTemplate(choice.ItemTemplateId) is not { } picked
                || (picked.Flags & ItemTemplateFlags.Unique) != 0)
            {
                return QuestResult.Error;
            }

            paid.Add((picked, choice.Count));
        }

        var questItems = quest.Objectives
            .Where(o => o.Type == QuestObjectiveType.Collect)
            .Select(o => o.ItemTemplateId!.Value)
            .ToHashSet();
        CharacterInventoryContainer bag = character.Container(InventoryType.Bag);
        var bagAfter = bag.Items
            .Where(i => !questItems.Contains(i.TemplateId.Value))
            .ToDictionary(i => i.Slot);
        if (!Fits(bagAfter, bag.Capacity, rewards))
            return QuestResult.BagFull;

        ulong balance = character.Data?.Gold ?? 0;
        if (quest.RewardGold > maxGold || balance > maxGold - quest.RewardGold)
            return QuestResult.GoldCap;

        return QuestResult.Ok;
    }

    /// <summary>
    /// The entry of the quest's choice set the pick names (#711); null for no pick (0) or a pick outside the set.
    /// Walked by index, so it allocates nothing.
    /// </summary>
    public static QuestItemRewardView? Choice(QuestView quest, ulong choiceItemTemplateId)
    {
        if (choiceItemTemplateId == 0)
            return null;

        IReadOnlyList<QuestItemRewardView> choices = quest.ItemChoices;
        for (int i = 0; i < choices.Count; i++)
        {
            if (choices[i].ItemTemplateId.Value == choiceItemTemplateId)
                return choices[i];
        }

        return null;
    }

    /// <summary>
    /// Whether every reward fits, taken together and in order, as IInventoryService.TryAdd would place them: into
    /// partial stacks of the template first, then one free slot per new stack.
    /// </summary>
    public static bool Fits(IReadOnlyDictionary<byte, InventoryItem> bag, byte capacity,
        IReadOnlyList<(ItemTemplate Template, uint Count)> rewards)
    {
        long free = capacity - bag.Count;
        Dictionary<ulong, long> partialRoom = [];

        foreach ((ItemTemplate template, uint count) in rewards)
        {
            long max = InventoryMove.MaxStack(template);
            ulong id = template.Id.Value;
            if (!partialRoom.TryGetValue(id, out long room))
                room = bag.Values.Where(i => i.TemplateId.Value == id && i.Count < max).Sum(i => max - i.Count);

            long remaining = count;
            long intoPartial = Math.Min(room, remaining);
            remaining -= intoPartial;
            room -= intoPartial;

            long stacks = (remaining + max - 1) / max;
            if (stacks > free)
                return false;

            free -= stacks;
            partialRoom[id] = room + (stacks * max - remaining);
        }

        return true;
    }
}
