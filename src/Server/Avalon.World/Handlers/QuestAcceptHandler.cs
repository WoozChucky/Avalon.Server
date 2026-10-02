using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Quest;
using Avalon.World.Public;
using Avalon.World.Quests;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>CMSG_QUEST_ACCEPT (#433): one SMSG_QUEST_RESULT.</summary>
[PacketHandler(NetworkPacketType.CMSG_QUEST_ACCEPT)]
public class QuestAcceptHandler(QuestService quests, ILogger<QuestAcceptHandler> logger) : WorldPacketHandler<CQuestAcceptPacket>
{
    public override void Execute(IWorldConnection connection, CQuestAcceptPacket packet) =>
        QuestReplies.Handle(connection, logger, NetworkPacketType.CMSG_QUEST_ACCEPT, packet.QuestId,
            character => quests.Accept(connection, character, packet.QuestId, packet.NpcGuid));
}
