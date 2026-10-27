using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Talents;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Talents;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>
/// Allocates talent nodes (#1181), all or nothing, on the tick, in the map pass. Every request gets exactly one
/// SMSG_TALENT_RESULT; a connection with no character gets nothing. TalentRules decides against every id before anything
/// changes, so a refusal changes nothing, marks no save and allocates nothing beyond its pooled reply. An accepted
/// request holds every node, then TalentChange refreshes the stats and the abilities and marks the talent state owed,
/// which the tick's TalentFlusher sends.
/// </summary>
[PacketHandler(NetworkPacketType.CMSG_TALENT_ALLOCATE)]
public sealed class TalentAllocateHandler(ILogger<TalentAllocateHandler> logger, IWorld world)
    : WorldPacketHandler<CTalentAllocatePacket>
{
    public override void Execute(IWorldConnection connection, CTalentAllocatePacket packet)
    {
        if (connection.Character is not CharacterEntity character)
        {
            logger.LogDebug("Dropped CMSG_TALENT_ALLOCATE from a connection with no character");
            return;
        }

        TalentResult result;
        uint refusedNode;
        try
        {
            result = Allocate(connection, character, packet, out refusedNode);
        }
        catch (Exception e)
        {
            // The request still gets its one reply, and nothing escapes onto the tick. The rules decided before anything
            // changed, so a throw there changed nothing; one after the nodes were held leaves them held, and the state
            // owed, so the window learns of them.
            logger.LogError(e, "CMSG_TALENT_ALLOCATE {RequestId} of {Count} nodes threw; answering InternalError",
                packet.RequestId, packet.NodeIds.Count);
            result = TalentResult.InternalError;
            refusedNode = 0;
        }

        connection.Send(STalentResultPacket.Create(packet.RequestId, result, refusedNode, PacketEncoder.Shared));
    }

    private TalentResult Allocate(IWorldConnection connection, CharacterEntity character, CTalentAllocatePacket packet,
        out uint refusedNode)
    {
        // One read: the tree, the abilities and the quests the points are priced by are one generation.
        StaticData data = world.Data;
        TalentAllocation talents = character.Talents;
        int earned = TalentPoints.Earned(character.Level, character.Quests, data.Quests);

        TalentResult result = TalentRules.Allocate(talents, data.Talents, character.Class, character.IsDead, earned,
            packet.NodeIds, out refusedNode);
        if (result != TalentResult.Ok || packet.NodeIds.Count == 0)
            return result;

        talents.Allocate(packet.NodeIds);
        TalentChange.Apply(connection, character, data, logger);
        return TalentResult.Ok;
    }
}
