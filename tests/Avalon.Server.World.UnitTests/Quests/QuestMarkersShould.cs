using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Quest;
using Avalon.World.Entities;
using Avalon.World.Public.Enums;
using Avalon.World.Quests;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>Markers per character over the quest NPCs in its instance (#433): sent on entry and whenever one changes, never repeated.</summary>
public class QuestMarkersShould
{
    private const uint Story = 7821, Chore = 7822;

    private static void Tick(QuestTestWorld w, QuestClient c) => QuestFlusher.Flush(c.Connection, w.Quests);

    private static QuestTemplate StoryQuest() => Quest(Story).WithStage(0, Kill(Story * 10 + 1, Boar, 1));

    private static QuestTemplate ChoreQuest() =>
        Quest(Chore).WithStage(0, Kill(Chore * 10 + 1, Boar, 1)).Repeating(QuestRepeatFrequency.Daily);

    /// <summary>Accepts the quest and kills its one boar: it is ready to turn in.</summary>
    private static void Ready(QuestTestWorld w, QuestClient c, uint id)
    {
        w.Accept(c, id);
        w.Quests.AddProgress(c.Character, id, id * 10 + 1, 1);
    }

    private static Dictionary<ulong, QuestMarker> Last(QuestClient c) =>
        c.Read<SQuestMarkersPacket>(NetworkPacketType.SMSG_QUEST_MARKERS).Last().Markers.ToDictionary(m => m.CreatureGuid, m => m.Marker);

    [Fact]
    public async Task Mark_each_quest_npc_for_this_character_and_leave_monsters_out()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        Creature giver = w.Place(Giver), ender = w.Place(Ender), boar = w.Place(Boar);
        QuestClient c = w.Join();

        Tick(w, c);

        Dictionary<ulong, QuestMarker> markers = Last(c);
        Assert.Equal(QuestMarker.Available, markers[giver.Guid.RawValue]);
        Assert.Equal(QuestMarker.None, markers[ender.Guid.RawValue]);
        Assert.False(markers.ContainsKey(boar.Guid.RawValue));
    }

    [Fact]
    public async Task Change_the_markers_as_the_quest_moves_and_send_nothing_when_nothing_changed()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        Creature giver = w.Place(Giver);
        QuestClient c = w.Join();
        Tick(w, c);

        Tick(w, c);
        Assert.Single(c.Read<SQuestMarkersPacket>(NetworkPacketType.SMSG_QUEST_MARKERS));

        w.Talk(c, giver);
        w.Quests.Accept(c.Connection, c.Character, Hunt, giver.Guid.RawValue);
        Tick(w, c);
        Assert.Equal(QuestMarker.None, Last(c)[giver.Guid.RawValue]);

        w.Quests.AddProgress(c.Character, Hunt, HuntKill, 2);
        Tick(w, c);
        Assert.Equal(QuestMarker.ReadyToTurnIn, Last(c)[giver.Guid.RawValue]);
        Assert.Equal(3, c.Read<SQuestMarkersPacket>(NetworkPacketType.SMSG_QUEST_MARKERS).Count);
    }

    [Fact]
    public async Task Mark_a_quest_as_available_once_the_character_reaches_its_level()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        Creature ender = w.Place(Ender);
        QuestClient c = w.Join(level: 1);
        QuestTestWorld.Complete(c, Hunt);
        QuestTestWorld.Complete(c, Tusks);
        Tick(w, c);
        Assert.Equal(QuestMarker.None, Last(c)[ender.Guid.RawValue]);   // Howl needs level 2

        c.Character.Level = 2;
        Tick(w, c);

        Assert.Equal(QuestMarker.Available, Last(c)[ender.Guid.RawValue]);
    }

    /// <summary>#710: story before chores within a tier, turn-in before offer.</summary>
    [Theory]
    [InlineData("story ready, chore ready", QuestMarker.ReadyToTurnIn)]
    [InlineData("chore ready, story offered", QuestMarker.RepeatableReadyToTurnIn)]
    [InlineData("story offered, chore offered", QuestMarker.Available)]
    [InlineData("chore offered", QuestMarker.RepeatableAvailable)]
    [InlineData("both done", QuestMarker.None)]
    public async Task Rank_story_markers_over_repeatable_ones_and_turn_ins_over_offers(string row, QuestMarker expected)
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync([StoryQuest(), ChoreQuest()]);
        QuestClient c = w.Join();
        DateTime now = w.Clock.GetUtcNow().UtcDateTime;
        switch (row)
        {
            case "story ready, chore ready": Ready(w, c, Story); Ready(w, c, Chore); break;
            case "chore ready, story offered": Ready(w, c, Chore); break;
            case "story offered, chore offered": break;
            case "chore offered": c.Character.Quests.Complete(Story, now); break;
            case "both done": c.Character.Quests.Complete(Story, now); c.Character.Quests.Complete(Chore, now); break;
        }

        Assert.Equal(expected, w.Quests.MarkerFor(c.Character, new CreatureTemplateId(Giver)));
    }
}
