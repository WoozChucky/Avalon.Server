using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Party;
using Avalon.World.Parties;
using Avalon.World.Public;

namespace Avalon.World.Handlers;

/// <summary>CMSG_PARTY_LEAVE (2026-09-30): one SMSG_PARTY_RESULT.</summary>
[PacketHandler(NetworkPacketType.CMSG_PARTY_LEAVE)]
public class PartyLeaveHandler(PartyService parties) : WorldPacketHandler<CPartyLeavePacket>
{
    public override void Execute(IWorldConnection connection, CPartyLeavePacket packet)
    {
        if (connection.Character is { } character)
            PartyReplies.Answer(connection, parties.Leave(character.Guid.Id), null);
    }
}
