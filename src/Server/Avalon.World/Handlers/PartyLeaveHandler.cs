using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Party;
using Avalon.World.Parties;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>CMSG_PARTY_LEAVE (2026-09-30): one SMSG_PARTY_RESULT.</summary>
[PacketHandler(NetworkPacketType.CMSG_PARTY_LEAVE)]
public class PartyLeaveHandler(PartyService parties, ILogger<PartyLeaveHandler> logger) : WorldPacketHandler<CPartyLeavePacket>
{
    public override void Execute(IWorldConnection connection, CPartyLeavePacket packet) =>
        PartyReplies.Handle(connection, logger, NetworkPacketType.CMSG_PARTY_LEAVE, character =>
            (parties.Leave(character.Guid.Id), null));
}
