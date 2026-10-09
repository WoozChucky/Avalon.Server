using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Quest;
using Avalon.World.Entities;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Quests;

/// <summary>The one SMSG_QUEST_RESULT a quest request is answered with (#433), as PartyReplies is for parties.</summary>
public static class QuestReplies
{
    /// <summary>
    /// Runs one quest request for the connection's character and answers it exactly once. A connection with no
    /// character is not answered. A request that throws is logged and answered Error (a turn-in that throws part way
    /// may have changed what it had already applied, as a vendor trade may); a throw from sending the answer itself
    /// is left to the caller, so nothing is answered twice.
    /// </summary>
    public static void Handle(IWorldConnection connection, ILogger logger, NetworkPacketType request, uint questId,
        Func<CharacterEntity, QuestResult> handle)
    {
        if (connection.Character is not CharacterEntity character)
            return;

        QuestResult result;
        try
        {
            result = handle(character);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Quest request {Request} for quest {QuestId} from character {CharacterId} failed",
                request, questId, character.Guid.Id);
            result = QuestResult.Error;
        }

        connection.Send(SQuestResultPacket.Create(result, questId, connection.CryptoSession.Encryptor));
    }
}
