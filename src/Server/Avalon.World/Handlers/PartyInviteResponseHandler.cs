using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Party;
using Avalon.World.Parties;
using Avalon.World.Public;

namespace Avalon.World.Handlers;

/// <summary>CMSG_PARTY_INVITE_RESPONSE (2026-09-30): one SMSG_PARTY_RESULT.</summary>
[PacketHandler(NetworkPacketType.CMSG_PARTY_INVITE_RESPONSE)]
public class PartyInviteResponseHandler(PartyService parties) : WorldPacketHandler<CPartyInviteResponsePacket>
{
    public override void Execute(IWorldConnection connection, CPartyInviteResponsePacket packet)
    {
        if (connection.Character is { } character)
            PartyReplies.Answer(connection, parties.Respond(character.Guid.Id, packet.Accept), null);
    }
}
