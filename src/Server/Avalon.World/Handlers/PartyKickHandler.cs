using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Party;
using Avalon.World.Parties;
using Avalon.World.Public;

namespace Avalon.World.Handlers;

/// <summary>CMSG_PARTY_KICK (2026-09-30): one SMSG_PARTY_RESULT, naming the member when the sender's party has it.</summary>
[PacketHandler(NetworkPacketType.CMSG_PARTY_KICK)]
public class PartyKickHandler(PartyService parties) : WorldPacketHandler<CPartyKickPacket>
{
    public override void Execute(IWorldConnection connection, CPartyKickPacket packet)
    {
        if (connection.Character is not { } character)
            return;

        string? name = parties.PartyOf(character.Guid.Id)?.Find(packet.CharacterId)?.Name;
        PartyReplies.Answer(connection, parties.Kick(character.Guid.Id, packet.CharacterId), name);
    }
}
