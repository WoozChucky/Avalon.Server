using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Party;
using Avalon.World.Parties;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>CMSG_PARTY_EXPERIENCE_MODE (2026-09-30): one SMSG_PARTY_RESULT.</summary>
[PacketHandler(NetworkPacketType.CMSG_PARTY_EXPERIENCE_MODE)]
public class PartyExperienceModeHandler(PartyService parties, ILogger<PartyExperienceModeHandler> logger) : WorldPacketHandler<CPartyExperienceModePacket>
{
    public override void Execute(IWorldConnection connection, CPartyExperienceModePacket packet) =>
        PartyReplies.Handle(connection, logger, NetworkPacketType.CMSG_PARTY_EXPERIENCE_MODE, character =>
            (parties.SetExperienceMode(character.Guid.Id, packet.Mode), null));
}
