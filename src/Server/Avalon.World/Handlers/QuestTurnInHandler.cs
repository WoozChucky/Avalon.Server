using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Quest;
using Avalon.World.Public;
using Avalon.World.Quests;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>CMSG_QUEST_TURN_IN (#433): one SMSG_QUEST_RESULT; a turn-in that throws is answered Error.</summary>
[PacketHandler(NetworkPacketType.CMSG_QUEST_TURN_IN)]
public class QuestTurnInHandler(QuestService quests, ILogger<QuestTurnInHandler> logger) : WorldPacketHandler<CQuestTurnInPacket>
{
    public override void Execute(IWorldConnection connection, CQuestTurnInPacket packet) =>
        QuestReplies.Handle(connection, logger, NetworkPacketType.CMSG_QUEST_TURN_IN, packet.QuestId,
            character => quests.TurnIn(connection, character, packet.QuestId, packet.NpcGuid));
}
