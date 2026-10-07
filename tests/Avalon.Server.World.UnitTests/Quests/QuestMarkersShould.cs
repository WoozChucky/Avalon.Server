using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Quest;
using Avalon.World.Entities;
using Avalon.World.Quests;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>Markers per character over the quest NPCs in its instance (#433): sent on entry and whenever one changes, never repeated.</summary>
public class QuestMarkersShould
{
    private static void Tick(QuestTestWorld w, QuestClient c) => QuestFlusher.Flush(c.Connection, w.Quests);

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
}
