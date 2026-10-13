using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Party;
using Avalon.World.Parties;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>CMSG_PARTY_KICK (2026-09-30): one SMSG_PARTY_RESULT, naming the member when the sender's party has it.</summary>
[PacketHandler(NetworkPacketType.CMSG_PARTY_KICK)]
public class PartyKickHandler(PartyService parties, ILogger<PartyKickHandler> logger) : WorldPacketHandler<CPartyKickPacket>
{
    public override void Execute(IWorldConnection connection, CPartyKickPacket packet) =>
        PartyReplies.Handle(connection, logger, NetworkPacketType.CMSG_PARTY_KICK, (parties, packet),
            static (character, request) =>
            {
                string? name = request.parties.PartyOf(character.Guid.Id)?.Find(request.packet.CharacterId)?.Name;
                return (request.parties.Kick(character.Guid.Id, request.packet.CharacterId), name);
            });
}
