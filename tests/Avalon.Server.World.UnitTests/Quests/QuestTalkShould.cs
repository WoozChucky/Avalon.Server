using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.World;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Quests;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>Talking to the named NPC completes a Talk objective of the current stage (#433), through a real interact.</summary>
public class QuestTalkShould
{
    private static async Task<(QuestTestWorld W, QuestClient C)> HowlAtStageAsync(int stage)
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join(level: 2);
        QuestTestWorld.Complete(c, Hunt);
        QuestTestWorld.Complete(c, Tusks);
        w.Accept(c, Howl);
        if (stage >= 1)
            w.Quests.AddProgress(c.Character, Howl, HowlKill, 1);
        return (w, c);
    }

    private static void Interact(QuestTestWorld w, QuestClient c, Creature npc, QuestService? quests = null) =>
        new InteractHandler(NullLogger<InteractHandler>.Instance, w.World, quests ?? w.Quests)
            .Execute(c.Connection, new CInteractPacket { TargetGuid = npc.Guid.RawValue });

    [Fact]
    public async Task Complete_the_talk_objective_and_start_the_next_stage()
    {
        (QuestTestWorld w, QuestClient c) = await HowlAtStageAsync(1);

        Interact(w, c, w.Place(TalkTarget));

        Assert.Equal(1u, c.Character.Quests.Get(Howl)!.ProgressOf(HowlTalk));
        Assert.Equal(2, c.Character.Quests.Get(Howl)!.Stage);
    }

    [Fact]
    public async Task Not_count_a_talk_in_an_earlier_stage()
    {
        (QuestTestWorld w, QuestClient c) = await HowlAtStageAsync(0);

        Interact(w, c, w.Place(TalkTarget));

        Assert.Equal(0u, c.Character.Quests.Get(Howl)!.ProgressOf(HowlTalk));
    }

    [Fact]
    public async Task Not_count_an_interact_the_npc_refused()
    {
        (QuestTestWorld w, QuestClient c) = await HowlAtStageAsync(1);

        Interact(w, c, w.Place(TalkTarget, new Vector3(0, 0, 50)));   // past the 5 m interact range

        Assert.Equal(0u, c.Character.Quests.Get(Howl)!.ProgressOf(HowlTalk));
    }

    /// <summary>A quest (or its script) that throws costs the talk its credit, never the conversation.</summary>
    [Fact]
    public async Task Still_open_the_conversation_when_the_quest_credit_throws()
    {
        (QuestTestWorld w, QuestClient c) = await HowlAtStageAsync(1);
        Creature npc = w.Place(TalkTarget);
        c.Clear();

        Interact(w, c, npc, w.ThrowingQuests());

        Assert.Single(c.Read<SDialogueNodePacket>(NetworkPacketType.SMSG_DIALOGUE_NODE));
        Assert.Equal(npc.Guid, c.Connection.CurrentDialogue!.Value.Npc);
        Assert.Equal(QuestTestWorld.TalkRoot, (int)c.Connection.CurrentDialogue!.Value.Node.Value);
        Assert.Equal(0u, c.Character.Quests.Get(Howl)!.ProgressOf(HowlTalk));
    }
}
