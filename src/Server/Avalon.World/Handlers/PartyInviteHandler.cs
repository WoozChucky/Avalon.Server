using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Party;
using Avalon.World.Parties;
using Avalon.World.Public;

namespace Avalon.World.Handlers;

/// <summary>CMSG_PARTY_INVITE (2026-09-30): one SMSG_PARTY_RESULT, naming the player invited.</summary>
[PacketHandler(NetworkPacketType.CMSG_PARTY_INVITE)]
public class PartyInviteHandler(PartyService parties) : WorldPacketHandler<CPartyInvitePacket>
{
    public override void Execute(IWorldConnection connection, CPartyInvitePacket packet)
    {
        if (connection.Character is not { } character)
            return;

        string name = packet.TargetName ?? string.Empty;
        PartyReplies.Answer(connection, parties.Invite(character.Guid.Id, name), name);
    }
}
