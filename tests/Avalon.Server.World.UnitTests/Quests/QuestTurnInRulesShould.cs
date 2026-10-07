using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Public.Characters;
using Avalon.World.Quests;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>Whether rewards fit a bag (#433): partial stacks first, then one free slot per new stack, several rewards together.</summary>
public class QuestTurnInRulesShould
{
    private static readonly ItemTemplate s_tonicItem = Items().Single(i => i.Id.Value == Tonic);   // stacks to 10
    private static readonly ItemTemplate s_charmItem = Items().Single(i => i.Id.Value == Charm);   // stacks to 1

    private static Dictionary<ushort, InventoryItem> Bag(params InventoryItem[] items) => items.ToDictionary(i => i.Slot);

    [Fact]
    public void Fit_into_a_partial_stack_without_a_free_slot() =>
        Assert.True(QuestTurnInRules.Fits(Bag(TestCharacters.Item(0, s_tonicItem, 7)), 1, [(s_tonicItem, 3)]));

    [Fact]
    public void Need_a_free_slot_for_what_the_partial_stack_cannot_hold() =>
        Assert.False(QuestTurnInRules.Fits(Bag(TestCharacters.Item(0, s_tonicItem, 7)), 1, [(s_tonicItem, 4)]));

    [Fact]
    public void Count_two_rewards_against_the_same_free_slots()
    {
        Assert.True(QuestTurnInRules.Fits(Bag(), 2, [(s_tonicItem, 10), (s_charmItem, 1)]));
        Assert.False(QuestTurnInRules.Fits(Bag(), 2, [(s_tonicItem, 11), (s_charmItem, 1)]));
    }

    [Fact]
    public void Let_a_second_reward_of_one_template_use_the_room_the_first_left() =>
        Assert.True(QuestTurnInRules.Fits(Bag(), 1, [(s_tonicItem, 4), (s_tonicItem, 6)]));
}
