using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Party;
using Avalon.World.Parties;
using Avalon.World.Public;

namespace Avalon.World.Handlers;

/// <summary>CMSG_PARTY_EXPERIENCE_MODE (2026-09-30): one SMSG_PARTY_RESULT.</summary>
[PacketHandler(NetworkPacketType.CMSG_PARTY_EXPERIENCE_MODE)]
public class PartyExperienceModeHandler(PartyService parties) : WorldPacketHandler<CPartyExperienceModePacket>
{
    public override void Execute(IWorldConnection connection, CPartyExperienceModePacket packet)
    {
        if (connection.Character is { } character)
            PartyReplies.Answer(connection, parties.SetExperienceMode(character.Guid.Id, packet.Mode), null);
    }
}
