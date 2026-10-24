using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Quest;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Loot;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Reload;

namespace Avalon.World.Quests;

/// <summary>
/// Whether a turn-in can go ahead, changing nothing (#433): ready; the last stage's Collect items all in the bag, in
/// stacks not offered in a trade (#1175: an offered stack is not the quest's to take, so it counts for nothing); the
/// pick one of the quest's choice set, or none for a quest without one (#711); every reward template, fixed and
/// picked, known, and the template it is paid as (a base's tier at the quest's level, #1179, <see cref="PaidAs" />) one a
/// quest may pay (not flagged Unique, not of Unique rarity); the fixed rewards and the pick, as paid, fit the bag as it
/// will be once every quest item has left it, less the slots a completing trade reserved; the gold under the cap,
/// counting the gold that trade is about to pay. Everything after it is ordered so nothing can refuse, as VendorRules is
/// for a trade.
/// </summary>
public static class QuestTurnInRules
{
    /// <param name="rewards">
    /// What the turn-in pays, in order: each reward's template as it is paid (<see cref="PaidAs" />) and its count.
    /// </param>
    public static QuestResult Decide(CharacterEntity character, QuestView quest, ActiveQuest active, ulong choiceItemTemplateId,
        ItemsPatch items, ulong maxGold, out IReadOnlyList<(ItemTemplate Template, uint Count)> rewards)
    {
        List<(ItemTemplate Template, uint Count)> paid = [];
        rewards = paid;
        if (active.State != CharacterQuestState.ReadyToTurnIn)
            return QuestResult.NotReady;

        foreach (QuestObjectiveView objective in quest.LastStage.Objectives)
        {
            if (objective.Type == QuestObjectiveType.Collect && Collectable(character, objective.ItemTemplateId!) < objective.Count)
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
            // refuse it after the quest items left (flagged Unique), or it would pay a unique (Unique rarity), so the
            // turn-in is refused instead. A base is judged as the tier it is paid as, which the catalog never saw.
            if (PaidAs(quest, reward, items) is not { } template || ItemUseRules.QuestRewardProblem(template) is not null)
                return QuestResult.Error;

            paid.Add((template, reward.Count));
        }

        // The pick is one more reward item, judged as the fixed ones are and placed after them.
        if (choice is not null)
        {
            if (PaidAs(quest, choice, items) is not { } picked || ItemUseRules.QuestRewardProblem(picked) is not null)
                return QuestResult.Error;

            paid.Add((picked, choice.Count));
        }

        var questItems = quest.Objectives
            .Where(o => o.Type == QuestObjectiveType.Collect)
            .Select(o => o.ItemTemplateId!.Value)
            .ToHashSet();
        CharacterInventoryContainer bag = character.Container(InventoryType.Bag);
        // A quest item offered in a trade stays behind (QuestService.RemoveQuestItems skips it), so it is no room.
        var bagAfter = new Dictionary<byte, InventoryItem>();
        foreach (InventoryItem item in bag.Items)
        {
            if (!questItems.Contains(item.TemplateId.Value) || character.Locks.IsLocked(item.InstanceId))
                bagAfter.Add(item.Slot, item);
        }
        if (!Fits(bagAfter, bag.Capacity, rewards, character.Locks))
            return QuestResult.BagFull;

        // IWallet.TryAddGold's rule: gold a completing trade is about to pay (#1175) counts as already in the balance.
        if (!character.Locks.CanReceiveGold(character.Data?.Gold ?? 0, quest.RewardGold, maxGold))
            return QuestResult.GoldCap;

        return QuestResult.Ok;
    }

    /// <summary>
    /// How a quest's base rewards are rolled (#1179): Magic outright, at the quest's level requirement, which the context
    /// holds at least 1. The turn-in rolls by it, and the reward window and the turn-in's checks resolve the paid tier by
    /// its item level, so all three agree.
    /// </summary>
    public static DropContext RewardRoll(QuestView quest) => new(quest.LevelRequirement, CreatureRarity.Normal, ForceMagic: true);

    /// <summary>
    /// The template <paramref name="reward" /> is paid as: a Common base (ItemRoller.IsRolledBase) is rolled Magic on the
    /// best tier of its line at the quest's level (ItemRoller.TierAt), any other template is paid as itself; null for a
    /// template a reload removed.
    /// </summary>
    public static ItemTemplate? PaidAs(QuestView quest, QuestItemRewardView reward, ItemsPatch items) =>
        items.Find(reward.ItemTemplateId) is { } named ? ItemRoller.TierAt(named, RewardRoll(quest).ItemLevel, items) : null;

    /// <summary>The item in the Bag the turn-in may take: every stack but those offered in a trade (#1175).</summary>
    private static long Collectable(CharacterEntity character, ItemTemplateId item) =>
        character.Locks.Any
            ? character.Locks.UnlockedCount(character.Container(InventoryType.Bag), item)
            : QuestService.HeldInBag(character, item);

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
    /// partial stacks of the template that are not offered in a trade first, then one free slot per new stack, leaving
    /// out the slots a completing trade reserved (#1175).
    /// </summary>
    public static bool Fits(IReadOnlyDictionary<byte, InventoryItem> bag, byte capacity,
        IReadOnlyList<(ItemTemplate Template, uint Count)> rewards, InventoryLocks locks)
    {
        long free = locks.FreeSlotCount(bag, capacity);
        Dictionary<ulong, long> partialRoom = [];

        foreach ((ItemTemplate template, uint count) in rewards)
        {
            // A rolled copy is one item in a slot of its own, whatever its template declares.
            long max = ItemRoller.IsRolledBase(template) ? 1 : InventoryMove.MaxStack(template);
            ulong id = template.Id.Value;
            if (!partialRoom.TryGetValue(id, out long room))
            {
                foreach (InventoryItem item in bag.Values)
                {
                    if (item.TemplateId.Value == id && item.Count < max && !locks.IsLocked(item.InstanceId))
                        room += max - item.Count;
                }
            }

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
