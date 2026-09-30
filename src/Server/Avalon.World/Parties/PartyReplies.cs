using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Party;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Parties;

/// <summary>The one SMSG_PARTY_RESULT a request is answered with, and the line a chat command adds on a refusal.</summary>
public static class PartyReplies
{
    /// <summary>
    /// Runs one party request for the connection's character and answers it exactly once. A connection with no
    /// character is not answered. A request that throws is logged and answered <see cref="PartyResult.Error" />;
    /// a throw from sending the answer itself is left to the caller, so nothing is answered twice.
    /// </summary>
    public static void Handle(IWorldConnection connection, ILogger logger, NetworkPacketType request,
        Func<ICharacter, (PartyResult Result, string? Name)> handle)
    {
        if (connection.Character is not { } character)
            return;

        PartyResult result;
        string? name;
        try
        {
            (result, name) = handle(character);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Party request {Request} from character {CharacterId} failed", request, character.Guid.Id);
            (result, name) = (PartyResult.Error, null);
        }

        Answer(connection, result, name);
    }

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
        PartyResult.Error => "That party request failed. Please try again.",
        _ => "That party request was not understood.",
    };
}
