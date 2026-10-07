using Avalon.Common.Mathematics;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Quest;
using Avalon.World.Entities;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>Accept re-checks everything at the giver (#433); abandon works anywhere.</summary>
public class QuestAcceptShould
{
    [Fact]
    public async Task Accept_a_quest_the_npc_gives_in_an_open_conversation()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        Creature giver = w.Place(Giver);
        w.Talk(c, giver);

        QuestResult result = w.Quests.Accept(c.Connection, c.Character, Hunt, giver.Guid.RawValue);

        Assert.Equal(QuestResult.Ok, result);
        Assert.Equal((CharacterQuestState.Active, 0), (c.Character.Quests.Get(Hunt)!.State, c.Character.Quests.Get(Hunt)!.Stage));
        Assert.Equal(w.Clock.GetUtcNow().UtcDateTime, c.Character.Quests.Get(Hunt)!.AcceptedAt);
        Assert.True(c.Character.SaveState.HasChanges);
        Assert.Contains("Quest accepted: A Test Quest.", c.Character.Quests.PendingLines);
    }

    [Fact]
    public async Task Refuse_without_an_open_conversation()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        Creature giver = w.Place(Giver);

        Assert.Equal(QuestResult.NoConversation, w.Quests.Accept(c.Connection, c.Character, Hunt, giver.Guid.RawValue));
        Assert.False(c.Character.Quests.IsActive(Hunt));
    }

    [Fact]
    public async Task Refuse_while_talking_to_another_npc()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        Creature giver = w.Place(Giver);
        w.Talk(c, w.Place(Ender));

        Assert.Equal(QuestResult.NoConversation, w.Quests.Accept(c.Connection, c.Character, Hunt, giver.Guid.RawValue));
    }

    [Fact]
    public async Task Refuse_past_the_leash_and_end_the_conversation()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        Creature giver = w.Place(Giver, new Vector3(0, 0, 6.5f));   // the leash is 6 m
        w.Talk(c, giver);

        Assert.Equal(QuestResult.TooFar, w.Quests.Accept(c.Connection, c.Character, Hunt, giver.Guid.RawValue));
        Assert.Null(c.Connection.CurrentDialogue);
        Assert.Contains(c.Sent, p => p.Header.Type == NetworkPacketType.SMSG_DIALOGUE_END);
        Assert.False(c.Character.Quests.IsActive(Hunt));
    }

    [Fact]
    public async Task Refuse_a_dead_character()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        Creature giver = w.Place(Giver);
        w.Talk(c, giver);
        c.Character.IsDead = true;

        Assert.Equal(QuestResult.NoConversation, w.Quests.Accept(c.Connection, c.Character, Hunt, giver.Guid.RawValue));
    }

    [Theory]
    [InlineData(Howl)]    // given by the ender, not this NPC
    [InlineData(99999u)]  // no such quest
    public async Task Refuse_a_quest_this_npc_does_not_give(uint questId)
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join(level: 5);
        Creature giver = w.Place(Giver);
        w.Talk(c, giver);

        Assert.Equal(QuestResult.NotAvailable, w.Quests.Accept(c.Connection, c.Character, questId, giver.Guid.RawValue));
    }

    [Fact]
    public async Task Refuse_when_the_log_is_full()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync(configure: c => c.MaxActiveQuests = 1);
        QuestClient c = w.Join();
        c.Character.Quests.Start(9999, DateTime.UnixEpoch);
        Creature giver = w.Place(Giver);
        w.Talk(c, giver);

        Assert.Equal(QuestResult.LogFull, w.Quests.Accept(c.Connection, c.Character, Hunt, giver.Guid.RawValue));
    }

    [Fact]
    public async Task Abandon_an_active_quest_anywhere_and_let_it_be_accepted_again()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        w.Accept(c, Hunt);
        w.Quests.AddProgress(c.Character, Hunt, HuntKill, 1);
        c.Connection.CurrentDialogue = null;

        Assert.Equal(QuestResult.Ok, w.Quests.Abandon(c.Character, Hunt));

        Assert.False(c.Character.Quests.IsActive(Hunt));
        Assert.Contains("Quest abandoned: A Test Quest.", c.Character.Quests.PendingLines);
        w.Accept(c, Hunt);
        Assert.Equal(0u, c.Character.Quests.Get(Hunt)!.ProgressOf(HuntKill));
    }

    [Fact]
    public async Task Answer_NotActive_to_abandoning_a_quest_not_held()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();

        Assert.Equal(QuestResult.NotActive, w.Quests.Abandon(w.Join().Character, Hunt));
    }

    /// <summary>Review Focus 2: a quest the catalog no longer has can still be dropped.</summary>
    [Fact]
    public async Task Abandon_a_quest_the_catalog_no_longer_has()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        c.Character.Quests.Start(99999, DateTime.UnixEpoch);

        Assert.Equal(QuestResult.Ok, w.Quests.Abandon(c.Character, 99999));
        Assert.False(c.Character.Quests.IsActive(99999));
    }

    /// <summary>
    /// Final review M1: once the quest is accepted, a throw while re-sending the giver's root is logged and the answer
    /// stays Ok, so the client is not told Error about an accept that happened.
    /// </summary>
    [Fact]
    public async Task Answer_Ok_when_resending_the_root_throws_after_the_accept()
    {
        var log = new TestLog();
        QuestTestWorld w = await QuestTestWorld.CreateAsync(log: log);
        QuestClient c = w.Join();
        Creature giver = w.Place(Giver);
        w.Talk(c, giver);
        c.Connection.When(x => x.Send(Arg.Is<NetworkPacket>(p => p.Header.Type == NetworkPacketType.SMSG_DIALOGUE_NODE)))
            .Do(_ => throw new InvalidOperationException("root unavailable"));

        QuestResult result = w.Quests.Accept(c.Connection, c.Character, Hunt, giver.Guid.RawValue);

        Assert.Equal(QuestResult.Ok, result);
        Assert.True(c.Character.Quests.IsActive(Hunt));
        Assert.Contains(log.Errors, e => e.Exception is InvalidOperationException);
    }
}
