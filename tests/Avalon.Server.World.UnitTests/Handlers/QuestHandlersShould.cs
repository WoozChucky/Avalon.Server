using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Quest;
using Avalon.Server.World.UnitTests.Quests;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Handlers;

/// <summary>Each quest request (#433) is answered with exactly one SMSG_QUEST_RESULT; a throw is answered Error; no character, nothing.</summary>
public class QuestHandlersShould
{
    private static List<SQuestResultPacket> Results(QuestClient c) => c.Read<SQuestResultPacket>(NetworkPacketType.SMSG_QUEST_RESULT);

    [Fact]
    public async Task Answer_each_request_exactly_once()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        Creature giver = w.Place(Giver);
        w.Talk(c, giver);

        new QuestAcceptHandler(w.Quests, NullLogger<QuestAcceptHandler>.Instance).Execute(c.Connection, new CQuestAcceptPacket { QuestId = Hunt, NpcGuid = giver.Guid.RawValue });
        new QuestTurnInHandler(w.Quests, NullLogger<QuestTurnInHandler>.Instance).Execute(c.Connection, new CQuestTurnInPacket { QuestId = Hunt, NpcGuid = giver.Guid.RawValue });
        new QuestAbandonHandler(w.Quests, NullLogger<QuestAbandonHandler>.Instance).Execute(c.Connection, new CQuestAbandonPacket { QuestId = Hunt });
        new QuestAbandonHandler(w.Quests, NullLogger<QuestAbandonHandler>.Instance).Execute(c.Connection, new CQuestAbandonPacket { QuestId = Hunt });

        Assert.Equal([QuestResult.Ok, QuestResult.NotReady, QuestResult.Ok, QuestResult.NotActive], Results(c).Select(r => r.Result));
        Assert.All(Results(c), r => Assert.Equal(Hunt, r.QuestId));
    }

    [Fact]
    public async Task Answer_Error_once_when_the_request_throws()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        c.Connection.CurrentDialogue.Returns(_ => throw new InvalidOperationException("broken connection state"));

        new QuestAcceptHandler(w.Quests, NullLogger<QuestAcceptHandler>.Instance).Execute(c.Connection, new CQuestAcceptPacket { QuestId = Hunt, NpcGuid = 1 });

        SQuestResultPacket result = Assert.Single(Results(c));
        Assert.Equal((QuestResult.Error, Hunt), (result.Result, result.QuestId));
    }
}
