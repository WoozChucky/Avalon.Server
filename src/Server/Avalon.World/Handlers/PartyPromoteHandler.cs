using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Party;
using Avalon.World.Parties;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>CMSG_PARTY_PROMOTE (2026-09-30): one SMSG_PARTY_RESULT, naming the member when the sender's party has it.</summary>
[PacketHandler(NetworkPacketType.CMSG_PARTY_PROMOTE)]
public class PartyPromoteHandler(PartyService parties, ILogger<PartyPromoteHandler> logger) : WorldPacketHandler<CPartyPromotePacket>
{
    public override void Execute(IWorldConnection connection, CPartyPromotePacket packet) =>
        PartyReplies.Handle(connection, logger, NetworkPacketType.CMSG_PARTY_PROMOTE, (parties, packet),
            static (character, request) =>
            {
                string? name = request.parties.PartyOf(character.Guid.Id)?.Find(request.packet.CharacterId)?.Name;
                return (request.parties.Promote(character.Guid.Id, request.packet.CharacterId), name);
            });
}
