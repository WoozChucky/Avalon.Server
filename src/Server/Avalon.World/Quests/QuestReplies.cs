using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Quest;
using Avalon.Network.Packets.Serialization;
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
    /// <remarks>
    /// What the request needs (the quest service, the packet) is handed over as <paramref name="state" /> and back to
    /// <paramref name="handle" />, which is written <c>static</c>, as for <c>PartyReplies.Handle</c> (#883): a lambda
    /// that captured them cost every quest packet a closure and a delegate. A value tuple is the usual state, and is
    /// never boxed.
    /// </remarks>
    public static void Handle<TState>(IWorldConnection connection, ILogger logger, NetworkPacketType request,
        uint questId, TState state, Func<CharacterEntity, TState, QuestResult> handle)
    {
        if (connection.Character is not CharacterEntity character)
            return;

        QuestResult result;
        try
        {
            result = handle(character, state);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Quest request {Request} for quest {QuestId} from character {CharacterId} failed",
                request, questId, character.Guid.Id);
            result = QuestResult.Error;
        }

        connection.Send(SQuestResultPacket.Create(result, questId, PacketEncoder.Shared));
    }
}
