using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Quest;
using Avalon.World.Public;
using Avalon.World.Quests;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>CMSG_QUEST_ABANDON (#433), anywhere: one SMSG_QUEST_RESULT.</summary>
[PacketHandler(NetworkPacketType.CMSG_QUEST_ABANDON)]
public class QuestAbandonHandler(QuestService quests, ILogger<QuestAbandonHandler> logger) : WorldPacketHandler<CQuestAbandonPacket>
{
    public override void Execute(IWorldConnection connection, CQuestAbandonPacket packet) =>
        QuestReplies.Handle(connection, logger, NetworkPacketType.CMSG_QUEST_ABANDON, packet.QuestId,
            character => quests.Abandon(character, packet.QuestId));
}
