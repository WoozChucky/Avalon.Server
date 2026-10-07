using Avalon.Common.Mathematics;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Quest;
using Avalon.Network.Packets.World;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>
/// Quest options are added to an NPC's root per character (#433): the ready turn-ins first, then the available
/// offers by level then id, all before the node's own options. Choosing one sends SMSG_QUEST_OFFER.
/// </summary>
public class QuestDialogueShould
{
    // At the ender: 7501 and 7502 at level 1, 7503 at level 2, and 7504 (given by the giver) handed in here.
    private static List<QuestTemplate> AtTheEnder() =>
    [
        Quest(7503, giver: Ender, level: 2).WithStage(0, Kill(75031, Boar, 1)),
        Quest(7502, giver: Ender).WithStage(0, Kill(75021, Boar, 1)),
        Quest(7501, giver: Ender).WithStage(0, Kill(75011, Boar, 1)),
        Quest(7504, ender: Ender).WithStage(0, Kill(75041, Boar, 1)).Paying(Tonic, 2),
    ];

    private static SDialogueNodePacket Interact(QuestTestWorld w, QuestClient c, Creature npc)
    {
        c.Clear();
        new InteractHandler(NullLogger<InteractHandler>.Instance, w.World, w.Quests)
            .Execute(c.Connection, new CInteractPacket { TargetGuid = npc.Guid.RawValue });
        return Assert.Single(c.Read<SDialogueNodePacket>(NetworkPacketType.SMSG_DIALOGUE_NODE));
    }

    private static void MakeReady(QuestTestWorld w, QuestClient c, uint questId, uint objectiveId)
    {
        w.Accept(c, questId);
        Assert.True(w.Quests.AddProgress(c.Character, questId, objectiveId, uint.MaxValue));   // capped at the count
        Assert.Equal(CharacterQuestState.ReadyToTurnIn, c.Character.Quests.Get(questId)!.State);
    }

    [Fact]
    public async Task List_turn_ins_first_then_offers_by_level_then_id_then_the_nodes_own_options()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync(AtTheEnder());
        QuestClient c = w.Join(level: 2);
        MakeReady(w, c, 7504, 75041);

        SDialogueNodePacket node = Interact(w, c, w.Place(Ender));

        Assert.Equal([-7504, -7501, -7502, -7503, 7412], node.Options.Select(o => o.OptionId));
        Assert.Equal(
            [DialogueOptionKind.QuestTurnIn, DialogueOptionKind.QuestOffer, DialogueOptionKind.QuestOffer, DialogueOptionKind.QuestOffer, DialogueOptionKind.Conversation],
            node.Options.Select(o => o.Kind));
        Assert.Equal(new uint?[] { 7504, 7501, 7502, 7503, null }, node.Options.Select(o => o.QuestId));
        Assert.Equal("A Test Quest", node.Options[0].Text);
    }

    [Fact]
    public async Task Offer_each_character_only_what_it_may_take()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync(AtTheEnder());
        QuestClient low = w.Join(1, level: 1);

        SDialogueNodePacket node = Interact(w, low, w.Place(Ender));

        Assert.Equal([-7501, -7502, 7412], node.Options.Select(o => o.OptionId));
    }

    [Fact]
    public async Task Send_the_offer_with_resolved_text_when_a_quest_option_is_chosen()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        Creature giver = w.Place(Giver);
        SDialogueNodePacket root = Interact(w, c, giver);

        new DialogueChooseHandler(NullLogger<DialogueChooseHandler>.Instance, w.World, null, w.Quests)
            .Execute(c.Connection, new CDialogueChoosePacket { TargetGuid = giver.Guid.RawValue, NodeId = root.NodeId, OptionId = -(int)Hunt });

        SQuestOfferPacket offer = Assert.Single(c.Read<SQuestOfferPacket>(NetworkPacketType.SMSG_QUEST_OFFER));
        Assert.Equal((Hunt, giver.Guid.RawValue, QuestOfferMode.Offer), (offer.QuestId, offer.NpcGuid, offer.Mode));
        Assert.Equal("A Test Quest", offer.Quest!.Title);
        Assert.Equal("Please do the thing, Tester1.", offer.Quest.Text);
        QuestObjectiveDto objective = Assert.Single(Assert.Single(offer.Quest.Stages).Objectives);
        Assert.Equal((HuntKill, QuestObjectiveKind.Kill, Boar, 2u, "Things done"),
            (objective.ObjectiveId, objective.Kind, objective.TargetId, objective.Count, objective.Text));
        Assert.Equal((50u, 30ul), (offer.Quest.Rewards!.Experience, offer.Quest.Rewards.Money));
        Assert.Equal((giver.Guid, new Avalon.Common.ValueObjects.DialogueNodeId(QuestTestWorld.GiverRoot)), c.Connection.CurrentDialogue);
    }

    [Fact]
    public async Task Send_a_turn_in_offer_with_the_completion_text()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        MakeReady(w, c, Hunt, HuntKill);
        Creature giver = w.Place(Giver);
        SDialogueNodePacket root = Interact(w, c, giver);

        new DialogueChooseHandler(NullLogger<DialogueChooseHandler>.Instance, w.World, null, w.Quests)
            .Execute(c.Connection, new CDialogueChoosePacket { TargetGuid = giver.Guid.RawValue, NodeId = root.NodeId, OptionId = -(int)Hunt });

        SQuestOfferPacket offer = Assert.Single(c.Read<SQuestOfferPacket>(NetworkPacketType.SMSG_QUEST_OFFER));
        Assert.Equal(QuestOfferMode.TurnIn, offer.Mode);
        Assert.Equal("Well done.", offer.Quest!.Text);
    }

    /// <summary>Review Focus 5: a forged quest option id is ignored, and nothing else happens.</summary>
    [Theory]
    [InlineData(-7299)]           // a quest that does not exist
    [InlineData(-(int)Tusks)]     // one this NPC gives, but not yet to this character
    [InlineData(int.MinValue)]
    public async Task Ignore_a_quest_option_the_npc_does_not_offer(int optionId)
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        Creature giver = w.Place(Giver);
        SDialogueNodePacket root = Interact(w, c, giver);
        c.Clear();

        new DialogueChooseHandler(NullLogger<DialogueChooseHandler>.Instance, w.World, null, w.Quests)
            .Execute(c.Connection, new CDialogueChoosePacket { TargetGuid = giver.Guid.RawValue, NodeId = root.NodeId, OptionId = optionId });

        Assert.Empty(c.Sent);
        Assert.Equal(root.NodeId, c.Connection.CurrentDialogue!.Value.Node.Value);
    }

    [Fact]
    public async Task Resend_the_root_without_the_quest_once_it_is_accepted()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        Creature giver = w.Place(Giver);
        Interact(w, c, giver);
        c.Clear();

        Assert.Equal(QuestResult.Ok, w.Quests.Accept(c.Connection, c.Character, Hunt, giver.Guid.RawValue));

        SDialogueNodePacket node = Assert.Single(c.Read<SDialogueNodePacket>(NetworkPacketType.SMSG_DIALOGUE_NODE));
        Assert.Equal([7411], node.Options.Select(o => o.OptionId));
    }

    private static void Choose(QuestTestWorld w, QuestClient c, Creature npc, int nodeId, int optionId) =>
        new DialogueChooseHandler(NullLogger<DialogueChooseHandler>.Instance, w.World, null, w.Quests)
            .Execute(c.Connection, new CDialogueChoosePacket { TargetGuid = npc.Guid.RawValue, NodeId = nodeId, OptionId = optionId });

    /// <summary>
    /// A quest option chosen past the dialogue leash, or once the NPC is dead, is not offered: the conversation ends
    /// out loud, as for any other choose there, and no quest work happens.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task End_the_conversation_and_offer_nothing_past_the_leash_or_after_the_npc_died(bool dead)
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        Creature giver = w.Place(Giver);
        SDialogueNodePacket root = Interact(w, c, giver);
        Assert.Contains(root.Options, o => o.OptionId == -(int)Hunt);
        c.Clear();
        if (dead)
            giver.CurrentHealth = 0;
        else
            c.Character.Position = new Vector3(Avalon.World.Dialogue.NpcInteraction.LeashRange + 1, 0, 0);

        Choose(w, c, giver, root.NodeId, -(int)Hunt);

        Assert.Empty(c.Read<SQuestOfferPacket>(NetworkPacketType.SMSG_QUEST_OFFER));
        Assert.Single(c.Read<SDialogueEndPacket>(NetworkPacketType.SMSG_DIALOGUE_END));
        Assert.Null(c.Connection.CurrentDialogue);
        Assert.False(c.Character.Quests.IsActive(Hunt));
        Assert.Empty(c.Character.Quests.ClientChanges);
    }

    /// <summary>
    /// An authored option that leads back to the root resends it with this character's quest options, as the
    /// interact does: the advance path asks the quest service too.
    /// </summary>
    [Fact]
    public async Task Show_the_quest_options_when_an_option_leads_back_to_the_root()
    {
        const int Aside = 7404;
        QuestTestWorld w = await QuestTestWorld.CreateAsync(dialogue: (nodes, options) =>
        {
            nodes.Add(new DialogueNode { Id = Aside, CreatureTemplateId = Giver, IsRoot = false, TextId = BodyText });
            options.Add(new DialogueOption { Id = 7414, NodeId = QuestTestWorld.GiverRoot, TextId = DoneText, NextNodeId = Aside, SortOrder = 1 });
            options.Add(new DialogueOption { Id = 7415, NodeId = Aside, TextId = DoneText, NextNodeId = QuestTestWorld.GiverRoot, SortOrder = 0 });
        });
        QuestClient c = w.Join();
        Creature giver = w.Place(Giver);
        SDialogueNodePacket root = Interact(w, c, giver);

        c.Clear();
        Choose(w, c, giver, root.NodeId, 7414);
        SDialogueNodePacket aside = Assert.Single(c.Read<SDialogueNodePacket>(NetworkPacketType.SMSG_DIALOGUE_NODE));
        Assert.Equal([7415], aside.Options.Select(o => o.OptionId));

        c.Clear();
        Choose(w, c, giver, Aside, 7415);
        SDialogueNodePacket back = Assert.Single(c.Read<SDialogueNodePacket>(NetworkPacketType.SMSG_DIALOGUE_NODE));
        Assert.Equal(QuestTestWorld.GiverRoot, back.NodeId);
        Assert.Equal([-(int)Hunt, 7411, 7414], back.Options.Select(o => o.OptionId));
        Assert.Equal(DialogueOptionKind.QuestOffer, back.Options[0].Kind);
    }

    [Fact]
    public async Task Add_no_quest_options_to_a_node_that_is_not_the_root()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        Creature giver = w.Place(Giver);
        var notRoot = new Avalon.World.Public.Dialogue.DialogueNodeView(new(9999), giver.Metadata.Id, new(BodyText), []);

        Assert.Empty(w.Quests.DialogueOptionsFor(c.Connection, c.Character, giver, notRoot));
    }
}
