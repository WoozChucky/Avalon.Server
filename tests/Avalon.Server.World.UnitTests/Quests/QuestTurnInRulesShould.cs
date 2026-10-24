using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Quest;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Localization;
using Avalon.World.Quests;
using Avalon.World.Reload;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>
/// Whether rewards fit a bag (#433): partial stacks first, then one free slot per new stack, several rewards together; and
/// a completing trade's reservations (#1175) count as taken.
/// </summary>
public class QuestTurnInRulesShould
{
    private static readonly ItemTemplate s_tonicItem = Items().Single(i => i.Id.Value == Tonic);   // stacks to 10
    private static readonly ItemTemplate s_charmItem = Items().Single(i => i.Id.Value == Charm);   // stacks to 1

    private static readonly InventoryLocks s_nothingLocked = new();

    private static Dictionary<byte, InventoryItem> Bag(params InventoryItem[] items) => items.ToDictionary(i => i.Slot);

    [Fact]
    public void Fit_into_a_partial_stack_without_a_free_slot() =>
        Assert.True(QuestTurnInRules.Fits(Bag(TestCharacters.Item(0, s_tonicItem, 7)), 1, [(s_tonicItem, 3)], s_nothingLocked));

    [Fact]
    public void Need_a_free_slot_for_what_the_partial_stack_cannot_hold() =>
        Assert.False(QuestTurnInRules.Fits(Bag(TestCharacters.Item(0, s_tonicItem, 7)), 1, [(s_tonicItem, 4)], s_nothingLocked));

    [Fact]
    public void Count_two_rewards_against_the_same_free_slots()
    {
        Assert.True(QuestTurnInRules.Fits(Bag(), 2, [(s_tonicItem, 10), (s_charmItem, 1)], s_nothingLocked));
        Assert.False(QuestTurnInRules.Fits(Bag(), 2, [(s_tonicItem, 11), (s_charmItem, 1)], s_nothingLocked));
    }

    [Fact]
    public void Let_a_second_reward_of_one_template_use_the_room_the_first_left() =>
        Assert.True(QuestTurnInRules.Fits(Bag(), 1, [(s_tonicItem, 4), (s_tonicItem, 6)], s_nothingLocked));

    /// <summary>
    /// A trade (#1175) holds part of the Bag or the gold headroom: a completing one has reserved a Bag slot and gold
    /// headroom, and an open one has locked an offered stack. A turn-in made meanwhile counts each as taken. Without it,
    /// each case would be paid: one free slot for the Charm, 30 gold under the cap of 100, and room for one Tonic in the
    /// partial stack.
    /// </summary>
    [Theory]
    [InlineData("the only free slot is reserved", QuestResult.BagFull)]
    [InlineData("incoming gold would pass the cap", QuestResult.GoldCap)]
    [InlineData("the only partial stack is offered", QuestResult.BagFull)]
    public void Count_a_completing_trade_s_reservations_as_taken(string @case, QuestResult expected)
    {
        CharacterEntity character = TestCharacters.New(7, gold: 0);
        var bag = Enumerable.Range(0, 29).Select(s => TestCharacters.Item((byte)s, s_charmItem)).ToList();
        character.Quests.Load(new CharacterQuestRows(
            [new CharacterQuest { CharacterId = 7, QuestId = Tusks, State = CharacterQuestState.ReadyToTurnIn, AcceptedAt = DateTime.UnixEpoch }],
            [], []));
        ItemTemplate reward = s_charmItem;
        switch (@case)
        {
            case "the only free slot is reserved":
                character.Locks.Reserve(29);
                break;
            case "incoming gold would pass the cap":
                character.Locks.IncomingGold = 71;
                break;
            case "the only partial stack is offered":
                InventoryItem offered = TestCharacters.Item(29, s_tonicItem, 5);
                bag.Add(offered);
                character.Locks.Lock(offered.InstanceId);
                reward = s_tonicItem;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(@case), @case, null);
        }

        character.Container(InventoryType.Bag).Load(bag);
        var quest = new QuestView(Tusks, TitleText, BodyText, DoneText, new CreatureTemplateId(Giver), new CreatureTemplateId(Ender),
            1, null, null, 50, RewardGold: 30, null, [new QuestStageView(0, StageText, [])],
            [new QuestItemRewardView(reward.Id, 1)], [], QuestRepeat.Once);

        QuestResult result = QuestTurnInRules.Decide(character, quest, character.Quests.Get(Tusks)!, 0,
            new ItemsPatch(Items(), ItemModifierRepositories.EmptyCatalog()), maxGold: 100, out _);

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// #1179: a base reward is judged as the tier it is paid as, the best of its line the quest's level reaches (here the
    /// level-5 chest, for a quest of level 5 naming the level-1 one), never as the template the quest names: a reload that
    /// made that tier Unique refuses the turn-in, and each rolled copy takes a slot of its own, whatever the base stacks
    /// to. The reward window shows the named template, which a pick sends back, and the paid one.
    /// </summary>
    [Theory]
    [InlineData(false, 2u, 2, QuestResult.Ok)]
    [InlineData(true, 1u, 2, QuestResult.Error)]
    [InlineData(false, 2u, 1, QuestResult.BagFull)]
    public void Judge_a_base_reward_as_the_tier_it_is_paid_as(bool tierUnique, uint count, int freeSlots, QuestResult expected)
    {
        var named = new ItemTemplate
        {
            Id = new ItemTemplateId(7110),
            Name = "Plain Chest",
            Class = ItemClass.Armor,
            Slot = ItemSlotType.Chest,
            BaseKind = ItemBaseKind.PlateChest,
            RequiredLevel = 1,
            MaxStackSize = 5,
        };
        var tier = new ItemTemplate
        {
            Id = new ItemTemplateId(7111),
            Name = "Better Chest",
            Class = ItemClass.Armor,
            Slot = ItemSlotType.Chest,
            BaseKind = ItemBaseKind.PlateChest,
            RequiredLevel = 5,
            MaxStackSize = 5,
            Flags = tierUnique ? ItemTemplateFlags.Unique : ItemTemplateFlags.None,
        };
        var items = new ItemsPatch([named, tier, s_charmItem], ItemModifierRepositories.EmptyCatalog());
        CharacterEntity character = TestCharacters.New(7);
        character.Quests.Load(new CharacterQuestRows(
            [new CharacterQuest { CharacterId = 7, QuestId = Tusks, State = CharacterQuestState.ReadyToTurnIn, AcceptedAt = DateTime.UnixEpoch }],
            [], []));
        character.Container(InventoryType.Bag).Load(
            [.. Enumerable.Range(0, 30 - freeSlots).Select(s => TestCharacters.Item((byte)s, s_charmItem))]);
        var quest = new QuestView(Tusks, TitleText, BodyText, DoneText, new CreatureTemplateId(Giver), new CreatureTemplateId(Ender),
            5, null, null, 50, RewardGold: 0, null, [new QuestStageView(0, StageText, [])],
            [new QuestItemRewardView(named.Id, count)], [], QuestRepeat.Once);

        QuestResult result = QuestTurnInRules.Decide(character, quest, character.Quests.Get(Tusks)!, 0, items, maxGold: 100,
            out IReadOnlyList<(ItemTemplate Template, uint Count)> paid);
        QuestRewardItemDto offered = Assert.Single(QuestDisplay.Build(quest, items,
            NSubstitute.Substitute.For<ILocalizedTextCatalog>(), default, completionText: false).Rewards!.Items);

        Assert.Equal(expected, result);
        Assert.Equal((named.Id.Value, tier.Id.Value, ItemRarity.Magic), (offered.ItemTemplateId, offered.PaidItemTemplateId, offered.Rarity));
        if (expected == QuestResult.Ok)
            Assert.Equal([(tier, count)], paid);
    }
}
