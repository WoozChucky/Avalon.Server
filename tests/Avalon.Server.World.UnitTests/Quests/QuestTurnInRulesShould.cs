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
using Avalon.World.Quests;
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
    /// A trade completing for the character (#1175) has reserved a Bag slot and gold headroom: a turn-in made in that
    /// window counts both as taken. Without the reservation, each case would be paid: one free slot for the Charm, and
    /// 30 gold under the cap of 100.
    /// </summary>
    [Theory]
    [InlineData("the only free slot is reserved", QuestResult.BagFull)]
    [InlineData("incoming gold would pass the cap", QuestResult.GoldCap)]
    public void Count_a_completing_trade_s_reservations_as_taken(string @case, QuestResult expected)
    {
        CharacterEntity character = TestCharacters.New(7, gold: 0);
        character.Container(InventoryType.Bag).Load(
            Enumerable.Range(0, 29).Select(s => TestCharacters.Item((byte)s, s_charmItem)).ToList());
        character.Quests.Load(new CharacterQuestRows(
            [new CharacterQuest { CharacterId = 7, QuestId = Tusks, State = CharacterQuestState.ReadyToTurnIn, AcceptedAt = DateTime.UnixEpoch }],
            [], []));
        var quest = new QuestView(Tusks, TitleText, BodyText, DoneText, new CreatureTemplateId(Giver), new CreatureTemplateId(Ender),
            1, null, null, 50, RewardGold: 30, null, [new QuestStageView(0, StageText, [])],
            [new QuestItemRewardView(s_charmItem.Id, 1)], [], QuestRepeat.Once);
        switch (@case)
        {
            case "the only free slot is reserved":
                character.Locks.Reserve(29);
                break;
            case "incoming gold would pass the cap":
                character.Locks.IncomingGold = 71;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(@case), @case, null);
        }

        QuestResult result = QuestTurnInRules.Decide(character, quest, character.Quests.Get(Tusks)!, 0,
            id => Items().SingleOrDefault(i => i.Id == id), maxGold: 100, out _);

        Assert.Equal(expected, result);
    }
}
