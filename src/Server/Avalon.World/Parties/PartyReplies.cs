using Avalon.Network.Packets.Party;
using Avalon.World.Public;

namespace Avalon.World.Parties;

/// <summary>The one SMSG_PARTY_RESULT a request is answered with, and the line a chat command adds on a refusal.</summary>
public static class PartyReplies
{
    public static void Answer(IWorldConnection connection, PartyResult result, string? name) =>
        connection.Send(SPartyResultPacket.Create(result, name, connection.CryptoSession.Encrypt));

    public static string? Describe(PartyResult result, string? name) => result switch
    {
        PartyResult.Ok => null,
        PartyResult.NotLeader => "Only the party leader can do that.",
        PartyResult.NotFound => name is null ? "No such player." : $"Could not find {name}.",
        PartyResult.AlreadyInParty => $"{name ?? "That player"} is already in a party.",
        PartyResult.PartyFull => "The party is full.",
        PartyResult.InvitePending => $"{name ?? "That player"} already has a party invite.",
        PartyResult.NoInvite => "You have no party invite.",
        PartyResult.InCombat => "Not while a party member is in combat.",
        PartyResult.OnCooldown => "The experience mode was changed too recently.",
        PartyResult.Self => "You cannot do that to yourself.",
        PartyResult.NotInParty => "You are not in a party.",
        PartyResult.InviteExpired => $"The party invite with {name ?? "that player"} expired.",
        PartyResult.InviteDeclined => $"{name ?? "That player"} declined the party invite.",
        _ => "That party request was not understood.",
    };
}
