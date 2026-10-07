using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Quest;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Entities;
using Avalon.World.Public.Enums;
using Avalon.World.Quests;
using Avalon.World.Vendors;
using Microsoft.Extensions.Logging;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>
/// Turn-in (#433) is all or nothing: every check first (ready, items held, room, money cap), then the quest items
/// leave, then experience, money and items are paid and the quest is completed.
/// </summary>
public class QuestTurnInShould
{
    private static readonly ItemTemplateId TuskId = new(Tusk);

    /// <summary>Tusks accepted, both tusks in the bag (ready), and a conversation open with its ender.</summary>
    private static async Task<(QuestTestWorld W, QuestClient C, Creature Ender)> ReadyTusksAsync(
        List<QuestTemplate>? quests = null, ulong money = 0, TestLog? log = null, List<CharacterLevelExperience>? levels = null)
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync(quests, log: log, levels: levels);
        QuestClient c = w.Join(money: money);
        QuestTestWorld.Complete(c, Hunt);
        w.Accept(c, Tusks);
        w.Economy.InventoryOf(c.Character).TryAdd(TuskId, 2);
        QuestFlusher.Flush(c.Connection, w.Quests);
        Assert.Equal(CharacterQuestState.ReadyToTurnIn, c.Character.Quests.Get(Tusks)!.State);
        Creature ender = w.Place(Ender);
        w.Talk(c, ender);
        return (w, c, ender);
    }

    private static long Held(QuestClient c, ulong item) => QuestService.HeldInBag(c.Character, new ItemTemplateId(item));

    [Fact]
    public async Task Pay_experience_money_and_items_take_the_quest_items_and_complete()
    {
        (QuestTestWorld w, QuestClient c, Creature ender) = await ReadyTusksAsync();

        QuestResult result = w.Quests.TurnIn(c.Connection, c.Character, Tusks, ender.Guid.RawValue);

        Assert.Equal(QuestResult.Ok, result);
        Assert.Equal(0, Held(c, Tusk));
        Assert.Equal(2, Held(c, Tonic));
        Assert.Equal(30ul, c.Character.Data!.Money);
        Assert.Equal(50ul, c.Character.Experience);
        Assert.False(c.Character.Quests.IsActive(Tusks));
        Assert.True(c.Character.Quests.IsCompleted(Tusks));
        Assert.Contains("Quest completed: A Test Quest.", c.Character.Quests.PendingLines);
    }

    [Fact]
    public async Task Level_up_through_the_reward_and_carry_the_rest()
    {
        (QuestTestWorld w, QuestClient c, Creature ender) = await ReadyTusksAsync();
        c.Character.Experience = 390;   // level 1 needs 400; the reward is 50

        w.Quests.TurnIn(c.Connection, c.Character, Tusks, ender.Guid.RawValue);

        Assert.Equal((ushort)2, c.Character.Level);
        Assert.Equal(40ul, c.Character.Experience);
        Assert.Equal(900ul, c.Character.RequiredExperience);
    }

    /// <summary>#735: at the maximum level the reward pays no experience; money and items are paid in full.</summary>
    [Fact]
    public async Task Pay_money_and_items_but_no_experience_at_the_level_cap()
    {
        (QuestTestWorld w, QuestClient c, Creature ender) = await ReadyTusksAsync(
            levels: [new CharacterLevelExperience { Level = 1, Experience = 400 }]);
        c.Character.Experience = 390;   // the reward is 50, and none of it is given

        QuestResult result = w.Quests.TurnIn(c.Connection, c.Character, Tusks, ender.Guid.RawValue);

        Assert.Equal(QuestResult.Ok, result);
        Assert.Equal((ushort)1, c.Character.Level);
        Assert.Equal(390ul, c.Character.Experience);
        Assert.Equal(30ul, c.Character.Data!.Money);
        Assert.Equal(2, Held(c, Tonic));
        Assert.True(c.Character.Quests.IsCompleted(Tusks));
    }

    /// <summary>Review Focus 1: a tusk destroyed this tick, before any flush, still stops the turn-in.</summary>
    [Fact]
    public async Task Refuse_a_turn_in_whose_items_are_short_and_take_nothing()
    {
        (QuestTestWorld w, QuestClient c, Creature ender) = await ReadyTusksAsync();
        ushort slot = c.Character.Container(InventoryType.Bag).Items.Single().Slot;
        w.Economy.InventoryOf(c.Character).TryDestroy(new Avalon.World.Inventory.SlotRef(InventoryType.Bag, slot), 1, bankAccessible: false);

        Assert.Equal(QuestResult.NotReady, w.Quests.TurnIn(c.Connection, c.Character, Tusks, ender.Guid.RawValue));
        Assert.Equal(1, Held(c, Tusk));
        Assert.Equal(0ul, c.Character.Data!.Money);
        Assert.True(c.Character.Quests.IsActive(Tusks));
    }

    /// <summary>Review Focus 3: the slot the tusks free is room for the reward.</summary>
    [Fact]
    public async Task Count_the_slot_the_quest_items_free_as_room()
    {
        (QuestTestWorld w, QuestClient c, Creature ender) = await ReadyTusksAsync();
        ItemTemplate charm = Items().Single(i => i.Id.Value == Charm);
        CharacterInventoryContainer bag = c.Character.Container(InventoryType.Bag);
        bag.Load(bag.Items.Concat(Enumerable.Range(0, bag.Capacity).Select(s => (ushort)s)
            .Where(s => bag.Items.All(i => i.Slot != s)).Select(s => TestCharacters.Item(s, charm))).ToList());
        Assert.Empty(bag.FreeSlots());

        Assert.Equal(QuestResult.Ok, w.Quests.TurnIn(c.Connection, c.Character, Tusks, ender.Guid.RawValue));
        Assert.Equal(2, Held(c, Tonic));
    }

    [Fact]
    public async Task Refuse_a_turn_in_whose_rewards_do_not_fit_and_take_nothing()
    {
        List<QuestTemplate> quests = Chain();
        quests.Single(q => q.Id.Value == Tusks).ItemRewards[0].Count = 11;   // two stacks of Tonic; one slot will be free
        (QuestTestWorld w, QuestClient c, Creature ender) = await ReadyTusksAsync(quests);
        ItemTemplate charm = Items().Single(i => i.Id.Value == Charm);
        CharacterInventoryContainer bag = c.Character.Container(InventoryType.Bag);
        bag.Load(bag.Items.Concat(Enumerable.Range(0, bag.Capacity).Select(s => (ushort)s)
            .Where(s => bag.Items.All(i => i.Slot != s)).Select(s => TestCharacters.Item(s, charm))).ToList());

        Assert.Equal(QuestResult.BagFull, w.Quests.TurnIn(c.Connection, c.Character, Tusks, ender.Guid.RawValue));
        Assert.Equal(2, Held(c, Tusk));
        Assert.Equal(0ul, c.Character.Experience);
        Assert.Equal((ushort)1, c.Character.Level);
        Assert.Equal(0ul, c.Character.Data!.Money);
        Assert.True(c.Character.Quests.IsActive(Tusks));
    }

    /// <summary>Answered as Accept answers them: past the leash TooFar, a dead ender NoConversation; nothing taken or paid.</summary>
    [Theory]
    [InlineData(false, QuestResult.TooFar)]
    [InlineData(true, QuestResult.NoConversation)]
    public async Task Refuse_a_turn_in_past_the_leash_or_at_a_dead_ender_and_take_nothing(bool dead, QuestResult expected)
    {
        (QuestTestWorld w, QuestClient c, Creature ender) = await ReadyTusksAsync();
        if (dead)
            ender.CurrentHealth = 0;
        else
            c.Character.Position = new Avalon.Common.Mathematics.Vector3(Avalon.World.Dialogue.NpcInteraction.LeashRange + 1, 0, 0);

        Assert.Equal(expected, w.Quests.TurnIn(c.Connection, c.Character, Tusks, ender.Guid.RawValue));

        Assert.Null(c.Connection.CurrentDialogue);
        Assert.Equal(2, Held(c, Tusk));
        Assert.Equal(0, Held(c, Tonic));
        Assert.Equal(0ul, c.Character.Experience);
        Assert.Equal(0ul, c.Character.Data!.Money);
        Assert.True(c.Character.Quests.IsActive(Tusks));
        Assert.False(c.Character.Quests.IsCompleted(Tusks));
    }

    /// <summary>
    /// A reward a reload made Unique blocks the turn-in: answered Error, logged at Error naming the quest, and nothing
    /// is taken or paid.
    /// </summary>
    [Fact]
    public async Task Log_and_refuse_a_turn_in_whose_reward_a_reload_made_unique()
    {
        (QuestTestWorld w, QuestClient c, Creature ender) = await ReadyTusksAsync();
        w.Data.ItemTemplates.Single(t => t.Id.Value == Tonic).Flags |= ItemTemplateFlags.Unique;
        ILogger<QuestService> logger = NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<QuestService>>();
        var quests = new QuestService(w.World, NSubstitute.Substitute.For<IServiceProvider>(), w.Economy, w.Random, w.Clock, logger);

        Assert.Equal(QuestResult.Error, quests.TurnIn(c.Connection, c.Character, Tusks, ender.Guid.RawValue));

        Assert.Contains(logger.ReceivedCalls(), call => call.GetMethodInfo().Name == "Log"
            && (Microsoft.Extensions.Logging.LogLevel)call.GetArguments()[0]! == Microsoft.Extensions.Logging.LogLevel.Error
            && call.GetArguments()[2]!.ToString()!.Contains($"Quest {Tusks} ", StringComparison.Ordinal)
            && call.GetArguments()[2]!.ToString()!.Contains($"{Tonic}", StringComparison.Ordinal));
        Assert.Equal(2, Held(c, Tusk));
        Assert.Equal(0, Held(c, Tonic));
        Assert.Equal(0ul, c.Character.Experience);
        Assert.Equal(0ul, c.Character.Data!.Money);
        Assert.True(c.Character.Quests.IsActive(Tusks));
    }

    /// <summary>Review Focus 3.</summary>
    [Fact]
    public async Task Refuse_a_turn_in_that_would_pass_the_money_cap_and_take_nothing()
    {
        (QuestTestWorld w, QuestClient c, Creature ender) = await ReadyTusksAsync(money: new Avalon.World.Configuration.GameConfiguration().MaxMoney - 10);

        Assert.Equal(QuestResult.MoneyCap, w.Quests.TurnIn(c.Connection, c.Character, Tusks, ender.Guid.RawValue));
        Assert.Equal(2, Held(c, Tusk));
        Assert.Equal(0ul, c.Character.Experience);
        Assert.True(c.Character.Quests.IsActive(Tusks));
    }

    /// <summary>Review Focus 5.</summary>
    [Fact]
    public async Task Refuse_a_turn_in_at_an_npc_that_does_not_end_the_quest()
    {
        (QuestTestWorld w, QuestClient c, Creature _) = await ReadyTusksAsync();
        Creature giver = w.Place(Giver);
        w.Talk(c, giver);

        Assert.Equal(QuestResult.NoConversation, w.Quests.TurnIn(c.Connection, c.Character, Tusks, giver.Guid.RawValue));
        Assert.True(c.Character.Quests.IsActive(Tusks));
    }

    /// <summary>
    /// The catalog refuses a Unique reward, but /reload items can make one Unique afterwards; TryAdd would then refuse
    /// it once the quest items had gone, so the rule refuses the turn-in before anything moves.
    /// </summary>
    [Fact]
    public async Task Refuse_a_reward_a_reload_made_unique()
    {
        (QuestTestWorld w, QuestClient c, Creature _) = await ReadyTusksAsync();
        Assert.True(w.Data.Quests.TryGet(Tusks, out QuestView? quest));
        ItemTemplate unique = Items().Single(i => i.Id.Value == Tonic);
        unique.Flags |= ItemTemplateFlags.Unique;

        Assert.Equal(QuestResult.Error, QuestTurnInRules.Decide(c.Character, quest!, c.Character.Quests.Get(Tusks)!,
            _ => unique, ulong.MaxValue, out _));
    }

    [Fact]
    public async Task Refuse_a_quest_that_is_not_held_or_not_ready()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        Creature giver = w.Place(Giver);
        w.Talk(c, giver);
        Assert.Equal(QuestResult.NotActive, w.Quests.TurnIn(c.Connection, c.Character, Hunt, giver.Guid.RawValue));

        w.Accept(c, Hunt);
        w.Talk(c, giver);
        Assert.Equal(QuestResult.NotReady, w.Quests.TurnIn(c.Connection, c.Character, Hunt, giver.Guid.RawValue));
    }

    [Fact]
    public async Task Unlock_a_quest_gated_vendor_row_once_the_quest_is_completed()
    {
        (QuestTestWorld w, QuestClient c, Creature ender) = await ReadyTusksAsync();
        var row = new VendorStockView(1, 1, new ItemTemplateId(Tonic), null, null, null,
            new QuestRequirement(Tusks, QuestRequirementState.Completed), []);
        VendorStockView activeRow = row with { Requirement = new QuestRequirement(Tusks, QuestRequirementState.Active) };
        Assert.False(VendorRules.IsVisible(row, c.Character, QuestProgress.Instance));
        Assert.True(VendorRules.IsVisible(activeRow, c.Character, QuestProgress.Instance));

        w.Quests.TurnIn(c.Connection, c.Character, Tusks, ender.Guid.RawValue);

        Assert.True(VendorRules.IsVisible(row, c.Character, QuestProgress.Instance));
        Assert.False(VendorRules.IsVisible(activeRow, c.Character, QuestProgress.Instance));
    }

    /// <summary>
    /// Final review M1: once the quest is completed and paid, a throw while re-sending the ender's root is logged and
    /// the answer stays Ok, so the client is not told Error about a turn-in that happened.
    /// </summary>
    [Fact]
    public async Task Answer_Ok_when_resending_the_root_throws_after_the_turn_in()
    {
        var log = new TestLog();
        (QuestTestWorld w, QuestClient c, Creature ender) = await ReadyTusksAsync(log: log);
        c.Connection.When(x => x.Send(Arg.Is<Avalon.Network.Packets.Abstractions.NetworkPacket>(
                p => p.Header.Type == Avalon.Network.Packets.Abstractions.NetworkPacketType.SMSG_DIALOGUE_NODE)))
            .Do(_ => throw new InvalidOperationException("root unavailable"));

        QuestResult result = w.Quests.TurnIn(c.Connection, c.Character, Tusks, ender.Guid.RawValue);

        Assert.Equal(QuestResult.Ok, result);
        Assert.True(c.Character.Quests.IsCompleted(Tusks));
        Assert.Contains(log.Errors, e => e.Exception is InvalidOperationException);
    }
}
