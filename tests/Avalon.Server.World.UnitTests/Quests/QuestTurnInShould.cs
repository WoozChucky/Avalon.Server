using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Quest;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Dialogue;
using Avalon.World.Quests;
using Avalon.World.Vendors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>
/// Turn-in (#433) is all or nothing: every check first (ready, items held, the pick, reward templates, room, gold
/// cap), then the quest items leave, then experience, gold and items are paid and the quest is completed.
/// </summary>
public class QuestTurnInShould
{
    private static readonly ItemTemplateId s_tuskId = new(Tusk);

    /// <summary>Tusks accepted, both tusks in the bag (ready), and a conversation open with its ender.</summary>
    private static async Task<(QuestTestWorld W, QuestClient C, Creature Ender)> ReadyTusksAsync(
        List<QuestTemplate>? quests = null, ulong gold = 0, TestLog? log = null, List<CharacterLevelExperience>? levels = null)
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync(quests, log: log, levels: levels);
        QuestClient c = w.Join(gold: gold);
        QuestTestWorld.Complete(c, Hunt);
        w.Accept(c, Tusks);
        w.Economy.InventoryOf(c.Character).TryAdd(s_tuskId, 2);
        QuestFlusher.Flush(c.Connection, w.Quests);
        Assert.Equal(CharacterQuestState.ReadyToTurnIn, c.Character.Quests.Get(Tusks)!.State);
        Creature ender = w.Place(Ender);
        w.Talk(c, ender);
        return (w, c, ender);
    }

    private static long Held(QuestClient c, ulong item) => QuestService.HeldInBag(c.Character, new ItemTemplateId(item));

    /// <summary>Fills every free bag slot with a charm, so the only room left is the slot the tusks free.</summary>
    private static void FillBagWithCharms(QuestClient c)
    {
        ItemTemplate charm = Items().Single(i => i.Id.Value == Charm);
        CharacterInventoryContainer bag = c.Character.Container(InventoryType.Bag);
        bag.Load(bag.Items.Concat(Enumerable.Range(0, bag.Capacity).Select(s => (byte)s)
            .Where(s => bag.Items.All(i => i.Slot != s)).Select(s => TestCharacters.Item(s, charm))).ToList());
    }

    [Fact]
    public async Task Pay_experience_gold_and_items_take_the_quest_items_and_complete()
    {
        (QuestTestWorld w, QuestClient c, Creature ender) = await ReadyTusksAsync();

        QuestResult result = w.Quests.TurnIn(c.Connection, c.Character, Tusks, ender.Guid.RawValue);

        Assert.Equal(QuestResult.Ok, result);
        Assert.Equal(0, Held(c, Tusk));
        Assert.Equal(2, Held(c, Tonic));
        Assert.Equal(30ul, c.Character.Data!.Gold);
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

    /// <summary>#735: at the maximum level the reward pays no experience; gold and items are paid in full.</summary>
    [Fact]
    public async Task Pay_gold_and_items_but_no_experience_at_the_level_cap()
    {
        (QuestTestWorld w, QuestClient c, Creature ender) = await ReadyTusksAsync(
            levels: [new CharacterLevelExperience { Level = 1, Experience = 400 }]);
        c.Character.Experience = 390;   // the reward is 50, and none of it is given

        QuestResult result = w.Quests.TurnIn(c.Connection, c.Character, Tusks, ender.Guid.RawValue);

        Assert.Equal(QuestResult.Ok, result);
        Assert.Equal((ushort)1, c.Character.Level);
        Assert.Equal(390ul, c.Character.Experience);
        Assert.Equal(30ul, c.Character.Data!.Gold);
        Assert.Equal(2, Held(c, Tonic));
        Assert.True(c.Character.Quests.IsCompleted(Tusks));
    }

    /// <summary>Review Focus 1: a tusk destroyed this tick, before any flush, still stops the turn-in.</summary>
    [Fact]
    public async Task Refuse_a_turn_in_whose_items_are_short_and_take_nothing()
    {
        (QuestTestWorld w, QuestClient c, Creature ender) = await ReadyTusksAsync();
        byte slot = c.Character.Container(InventoryType.Bag).Items.Single().Slot;
        w.Economy.InventoryOf(c.Character).TryDestroy(new Avalon.World.Inventory.SlotRef(InventoryType.Bag, slot), 1, bankAccessible: false);

        Assert.Equal(QuestResult.NotReady, w.Quests.TurnIn(c.Connection, c.Character, Tusks, ender.Guid.RawValue));
        Assert.Equal(1, Held(c, Tusk));
        Assert.Equal(0ul, c.Character.Data!.Gold);
        Assert.True(c.Character.Quests.IsActive(Tusks));
    }

    /// <summary>
    /// #1175: a Collect stack offered in a trade is not the quest's to take. The turn-in counts only the stacks left
    /// free, answers NotReady and takes nothing; an abandon takes the free stacks and leaves the offered one.
    /// </summary>
    [Fact]
    public async Task Refuse_a_turn_in_while_a_collect_item_is_offered_in_a_trade()
    {
        (QuestTestWorld w, QuestClient c, Creature ender) = await ReadyTusksAsync();
        ItemTemplate tusk = TuskItem();
        Avalon.World.Public.Characters.InventoryItem offered = TestCharacters.Item(0, tusk);
        c.Character.Container(InventoryType.Bag).Load([offered, TestCharacters.Item(1, tusk)]);
        c.Character.Locks.Lock(offered.InstanceId);

        Assert.Equal(QuestResult.NotReady, w.Quests.TurnIn(c.Connection, c.Character, Tusks, ender.Guid.RawValue));
        Assert.Equal(2, Held(c, Tusk));
        Assert.Equal(0, Held(c, Tonic));
        Assert.Equal(0ul, c.Character.Data!.Gold);
        Assert.True(c.Character.Quests.IsActive(Tusks));

        Assert.Equal(QuestResult.Ok, w.Quests.Abandon(c.Character, Tusks));
        Assert.Equal(1, Held(c, Tusk));
        Assert.True(c.Character.Container(InventoryType.Bag).TryGet(0, out Avalon.World.Public.Characters.InventoryItem kept));
        Assert.Equal(offered.InstanceId, kept.InstanceId);
    }

    /// <summary>Review Focus 3: the slot the tusks free is room for the reward.</summary>
    [Fact]
    public async Task Count_the_slot_the_quest_items_free_as_room()
    {
        (QuestTestWorld w, QuestClient c, Creature ender) = await ReadyTusksAsync();
        FillBagWithCharms(c);
        Assert.Empty(c.Character.Container(InventoryType.Bag).FreeSlots());

        Assert.Equal(QuestResult.Ok, w.Quests.TurnIn(c.Connection, c.Character, Tusks, ender.Guid.RawValue));
        Assert.Equal(2, Held(c, Tonic));
    }

    [Fact]
    public async Task Refuse_a_turn_in_whose_rewards_do_not_fit_and_take_nothing()
    {
        List<QuestTemplate> quests = Chain();
        quests.Single(q => q.Id.Value == Tusks).ItemRewards[0].Count = 11;   // two stacks of Tonic; one slot will be free
        (QuestTestWorld w, QuestClient c, Creature ender) = await ReadyTusksAsync(quests);
        FillBagWithCharms(c);

        Assert.Equal(QuestResult.BagFull, w.Quests.TurnIn(c.Connection, c.Character, Tusks, ender.Guid.RawValue));
        Assert.Equal(2, Held(c, Tusk));
        Assert.Equal(0ul, c.Character.Experience);
        Assert.Equal((ushort)1, c.Character.Level);
        Assert.Equal(0ul, c.Character.Data!.Gold);
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
        Assert.Equal(0ul, c.Character.Data!.Gold);
        Assert.True(c.Character.Quests.IsActive(Tusks));
        Assert.False(c.Character.Quests.IsCompleted(Tusks));
    }

    /// <summary>
    /// A reward a reload made Unique blocks the turn-in, flagged Unique (own one copy) or of Unique rarity (#1179): answered
    /// Error, logged at Error naming the quest, and nothing is taken or paid.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Log_and_refuse_a_turn_in_whose_reward_a_reload_made_unique(bool byRarity)
    {
        (QuestTestWorld w, QuestClient c, Creature ender) = await ReadyTusksAsync();
        ItemTemplate tonic = w.Data.ItemTemplates.Single(t => t.Id.Value == Tonic);
        if (byRarity)
            tonic.Rarity = ItemRarity.Unique;
        else
            tonic.Flags |= ItemTemplateFlags.Unique;
        ILogger<QuestService> logger = NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<QuestService>>();
        var quests = new QuestService(w.World, NSubstitute.Substitute.For<IServiceProvider>(), w.Economy, w.Random, w.Roller, w.Clock, logger);

        Assert.Equal(QuestResult.Error, quests.TurnIn(c.Connection, c.Character, Tusks, ender.Guid.RawValue));

        Assert.Contains(logger.ReceivedCalls(), call => call.GetMethodInfo().Name == "Log"
            && (Microsoft.Extensions.Logging.LogLevel)call.GetArguments()[0]! == Microsoft.Extensions.Logging.LogLevel.Error
            && call.GetArguments()[2]!.ToString()!.Contains($"Quest {Tusks} ", StringComparison.Ordinal)
            && call.GetArguments()[2]!.ToString()!.Contains($"{Tonic}", StringComparison.Ordinal));
        Assert.Equal(2, Held(c, Tusk));
        Assert.Equal(0, Held(c, Tonic));
        Assert.Equal(0ul, c.Character.Experience);
        Assert.Equal(0ul, c.Character.Data!.Gold);
        Assert.True(c.Character.Quests.IsActive(Tusks));
    }

    /// <summary>The room the bag has once the tusks leave it.</summary>
    public enum BagRoom
    {
        Plenty,

        /// <summary>Every free slot holds a charm: the slot the tusks free holds the Tonic and nothing else.</summary>
        FixedItemsOnly,

        /// <summary>As FixedItemsOnly, but Tusks pays 11 Tonic, two stacks for the one slot the tusks free.</summary>
        NotEvenFixedItems,
    }

    public static TheoryData<string, bool, ulong, BagRoom, bool, QuestResult> Picks => new()
    {
        // case, offers a choice set, pick, bag room, pick made Unique by a reload, answer
        { "a pick from the set", true, Charm, BagRoom.Plenty, false, QuestResult.Ok },
        { "no pick", true, 0, BagRoom.Plenty, false, QuestResult.InvalidChoice },
        { "a fixed reward as the pick", true, Tonic, BagRoom.Plenty, false, QuestResult.InvalidChoice },             // Review Focus 1
        { "a pick on a quest with no choice set", false, Charm, BagRoom.Plenty, false, QuestResult.InvalidChoice },
        { "a bad pick with a full bag", true, Tusk, BagRoom.NotEvenFixedItems, false, QuestResult.InvalidChoice },   // Review Focus 2
        { "room for the fixed items only", true, Charm, BagRoom.FixedItemsOnly, false, QuestResult.BagFull },
        { "a pick a reload made Unique", true, Blade, BagRoom.Plenty, true, QuestResult.Error },                     // Review Focus 3
    };

    /// <summary>
    /// #711, through the handler: a choice set pays the fixed items plus the one item picked from it, all or nothing.
    /// A missing pick, a pick outside the set, or a pick on a quest with no set is refused InvalidChoice before the
    /// bag is judged; the bag must hold the fixed items and the pick; a pick a reload made Unique is refused Error and
    /// logged naming the quest and the template.
    /// </summary>
    [Theory]
    [MemberData(nameof(Picks))]
#pragma warning disable xUnit1026 // the case names the row in the test output
    public async Task Pay_the_fixed_items_and_the_pick_or_refuse_and_change_nothing(string @case, bool offers, ulong pick,
        BagRoom room, bool pickUnique, QuestResult expected)
#pragma warning restore xUnit1026
    {
        var log = new TestLog();
        List<QuestTemplate> quests = Chain();
        QuestTemplate tusks = quests.Single(q => q.Id.Value == Tusks);
        if (offers)
            tusks.Offering((Charm, 1), (Blade, 1));
        if (room == BagRoom.NotEvenFixedItems)
            tusks.ItemRewards[0].Count = 11;
        (QuestTestWorld w, QuestClient c, Creature ender) = await ReadyTusksAsync(quests, log: log);
        if (room != BagRoom.Plenty)
            FillBagWithCharms(c);
        if (pickUnique)
            w.Data.ItemTemplates.Single(t => t.Id.Value == Blade).Flags |= ItemTemplateFlags.Unique;
        long charmsBefore = Held(c, Charm);

        new QuestTurnInHandler(w.Quests, NullLogger<QuestTurnInHandler>.Instance).Execute(c.Connection,
            new CQuestTurnInPacket { QuestId = Tusks, NpcGuid = ender.Guid.RawValue, ChoiceItemTemplateId = pick });

        SQuestResultPacket answer = Assert.Single(c.Read<SQuestResultPacket>(NetworkPacketType.SMSG_QUEST_RESULT));
        Assert.Equal(Tusks, answer.QuestId);
        Assert.Equal(expected, answer.Result);
        Assert.Equal(0, Held(c, Blade));
        if (expected == QuestResult.Ok)
        {
            Assert.Equal(0, Held(c, Tusk));
            Assert.Equal(2, Held(c, Tonic));
            Assert.Equal(1, Held(c, Charm));
            Assert.Equal(30ul, c.Character.Data!.Gold);
            Assert.Equal(50ul, c.Character.Experience);
            Assert.True(c.Character.Quests.IsCompleted(Tusks));
            return;
        }

        Assert.Equal(2, Held(c, Tusk));
        Assert.Equal(0, Held(c, Tonic));
        Assert.Equal(charmsBefore, Held(c, Charm));
        Assert.Equal(0ul, c.Character.Data!.Gold);
        Assert.Equal(0ul, c.Character.Experience);
        Assert.True(c.Character.Quests.IsActive(Tusks));
        Assert.False(c.Character.Quests.IsCompleted(Tusks));
        if (expected == QuestResult.Error)
        {
            Assert.Contains(log.Errors, e => e.Message.Contains($"Quest {Tusks} ", StringComparison.Ordinal)
                && e.Message.Contains($"{Blade}", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// #1179, over the World database's seed: a fixed or picked reward whose template is a Common base is rolled
    /// Magic at the quest's level as it is paid, as its own line's best tier there (Mother of Thorns' Copper Band at level 7
    /// is its line's T8, the Oakbark Band, never another line's ring), its implicit first, then one or two affixes the
    /// level allows; the offer says so. Every such reward arrives unidentified.
    /// </summary>
    [Fact]
    public void Roll_a_magic_reward_on_the_picked_base_at_the_quests_level()
    {
        const uint AlphasHowl = 3, MotherOfThorns = 8, EarnItFancy = 11;
        var w = QuestTestWorld.OverSeed();
        QuestClient c = w.Join(level: 8);

        Creature borin = ReadyAtSeed(w, c, AlphasHowl);
        DialogueNodeView root = w.Data.Dialogue.GetRoot(borin.Metadata.Id)!;
        Assert.True(w.Quests.TryChoose(c.Connection, c.Character, borin, root, -(int)AlphasHowl));
        QuestRewardsDto offered = Assert.Single(c.Read<SQuestOfferPacket>(NetworkPacketType.SMSG_QUEST_OFFER)).Quest!.Rewards!;
        Assert.All(offered.Items.Concat(offered.Choices), reward => Assert.Equal(ItemRarity.Magic, reward.Rarity));
        Assert.Equal(QuestResult.Ok, w.Quests.TurnIn(c.Connection, c.Character, AlphasHowl, borin.Guid.RawValue, choiceItemTemplateId: 32));
        AssertMagic(c, w, Paid(c, 32), itemLevel: 2);
        AssertMagic(c, w, Paid(c, 69), itemLevel: 2);

        Creature uriel = ReadyAtSeed(w, c, MotherOfThorns);
        Assert.Equal(QuestResult.Ok, w.Quests.TurnIn(c.Connection, c.Character, MotherOfThorns, uriel.Guid.RawValue));
        AssertMagic(c, w, Paid(c, 104), itemLevel: 7);

        Creature hilde = ReadyAtSeed(w, c, EarnItFancy);
        Assert.Equal(QuestResult.Ok, w.Quests.TurnIn(c.Connection, c.Character, EarnItFancy, hilde.Guid.RawValue, choiceItemTemplateId: 37));
        AssertMagic(c, w, Paid(c, 37), itemLevel: 4);
    }

    /// <summary>
    /// Starts a seeded quest and finishes every objective through the service (a Collect objective's items put in the
    /// Bag and recounted), then opens a conversation with its ender, which is returned.
    /// </summary>
    private static Creature ReadyAtSeed(QuestTestWorld w, QuestClient c, uint questId)
    {
        Assert.True(w.Data.Quests.TryGet(questId, out QuestView? quest));
        c.Character.Quests.Start(questId, DateTime.UnixEpoch);
        foreach (QuestObjectiveView objective in quest!.Stages.SelectMany(stage => stage.Objectives))
        {
            if (objective.Type == QuestObjectiveType.Collect)
            {
                w.Economy.InventoryOf(c.Character).TryAdd(objective.ItemTemplateId!, objective.Count);
                QuestFlusher.Flush(c.Connection, w.Quests);
            }
            else
            {
                Assert.True(w.Quests.AddProgress(c.Character, questId, objective.Id, objective.Count));
            }
        }

        Assert.Equal(CharacterQuestState.ReadyToTurnIn, c.Character.Quests.Get(questId)!.State);
        Creature ender = w.Place(quest.EnderCreatureId.Value);
        w.Talk(c, ender);
        return ender;
    }

    private static InventoryItem Paid(QuestClient c, ulong template) =>
        Assert.Single(c.Character.Container(InventoryType.Bag).Items, item => item.TemplateId.Value == template);

    /// <summary>Magic at the level, its tier's implicit first, then one or two affixes, each one the level allows.</summary>
    private static void AssertMagic(QuestClient c, QuestTestWorld w, InventoryItem item, ushort itemLevel)
    {
        Assert.Equal((ItemRarity.Magic, itemLevel, 1u, false), (item.Rarity, item.ItemLevel, item.Count, item.Identified));
        IReadOnlyList<ItemInstanceModifier> held = c.Character.ItemModifiers.Of(item.InstanceId);
        Assert.Equal((Assert.Single(w.Data.ItemModifiers.LinkedTo(item.TemplateId)).Id, (byte)0), (held[0].ModifierId, held[0].Ordinal));
        List<ItemModifier> affixes = [.. held.Skip(1).Select(m => w.Data.ItemModifiers.Find(m.ModifierId)!)];
        Assert.InRange(affixes.Count, 1, 2);
        Assert.All(affixes, affix =>
        {
            Assert.True(affix.Group.Kind is ItemModifierKind.Prefix or ItemModifierKind.Suffix, $"{affix.Id} is {affix.Group.Kind}");
            Assert.True(affix.RequiredItemLevel <= itemLevel, $"{affix.Id} needs item level {affix.RequiredItemLevel}");
        });
    }

    /// <summary>Review Focus 3.</summary>
    [Fact]
    public async Task Refuse_a_turn_in_that_would_pass_the_gold_cap_and_take_nothing()
    {
        (QuestTestWorld w, QuestClient c, Creature ender) = await ReadyTusksAsync(gold: new Avalon.World.Configuration.GameConfiguration().MaxGold - 10);

        Assert.Equal(QuestResult.GoldCap, w.Quests.TurnIn(c.Connection, c.Character, Tusks, ender.Guid.RawValue));
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
        c.Connection.When(x => x.Send(Arg.Is<Avalon.Network.Packets.Abstractions.OutboundPacket>(
                p => p.Header.Type == Avalon.Network.Packets.Abstractions.NetworkPacketType.SMSG_DIALOGUE_NODE)))
            .Do(_ => throw new InvalidOperationException("root unavailable"));

        QuestResult result = w.Quests.TurnIn(c.Connection, c.Character, Tusks, ender.Guid.RawValue);

        Assert.Equal(QuestResult.Ok, result);
        Assert.True(c.Character.Quests.IsCompleted(Tusks));
        Assert.Contains(log.Errors, e => e.Exception is InvalidOperationException);
    }
}
