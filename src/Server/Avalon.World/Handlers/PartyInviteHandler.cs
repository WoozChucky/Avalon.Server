using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Party;
using Avalon.World.Parties;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>CMSG_PARTY_INVITE (2026-09-30): one SMSG_PARTY_RESULT, naming the player invited.</summary>
[PacketHandler(NetworkPacketType.CMSG_PARTY_INVITE)]
public class PartyInviteHandler(PartyService parties, ILogger<PartyInviteHandler> logger) : WorldPacketHandler<CPartyInvitePacket>
{
    public override void Execute(IWorldConnection connection, CPartyInvitePacket packet) =>
        PartyReplies.Handle(connection, logger, NetworkPacketType.CMSG_PARTY_INVITE, character =>
        {
            string name = packet.TargetName ?? string.Empty;
            return (parties.Invite(character.Guid.Id, name), name);
        });
}
