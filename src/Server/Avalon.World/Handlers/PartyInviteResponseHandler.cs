using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Party;
using Avalon.World.Parties;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>CMSG_PARTY_INVITE_RESPONSE (2026-09-30): one SMSG_PARTY_RESULT.</summary>
[PacketHandler(NetworkPacketType.CMSG_PARTY_INVITE_RESPONSE)]
public class PartyInviteResponseHandler(PartyService parties, ILogger<PartyInviteResponseHandler> logger) : WorldPacketHandler<CPartyInviteResponsePacket>
{
    public override void Execute(IWorldConnection connection, CPartyInviteResponsePacket packet) =>
        PartyReplies.Handle(connection, logger, NetworkPacketType.CMSG_PARTY_INVITE_RESPONSE, character =>
            (parties.Respond(character.Guid.Id, packet.Accept), null));
}
