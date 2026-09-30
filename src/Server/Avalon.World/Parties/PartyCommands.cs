using Avalon.Network.Packets.Party;
using Avalon.Network.Packets.Social;
using Avalon.World.Chat;

namespace Avalon.World.Parties;

/// <summary>
/// What every party command answers with: the SMSG_PARTY_RESULT the matching packet request gets, plus a line on
/// a refusal. A command that throws is left to CommandDispatcher, which logs it and tells staff only.
/// </summary>
internal static class PartyCommandReply
{
    // No character has id 0, so a name the caller's party does not hold reaches PartyService as a member it cannot
    // find, and is refused in the service's own order (not in a party, not the leader, yourself, not found), the
    // order the packet requests are answered in.
    private const uint NoCharacter = 0;

    public static void Send(CommandContext ctx, PartyResult result, string? name)
    {
        PartyReplies.Answer(ctx.Connection, result, name);
        if (PartyReplies.Describe(result, name) is { } line)
            ctx.Reply(line);
    }

    public static bool TryCharacter(CommandContext ctx, out uint id)
    {
        id = ctx.Connection.Character?.Guid.Id ?? 0;
        return ctx.Connection.Character is not null;
    }

    /// <summary>The member of the caller's party the name matches, ignoring case, or none.</summary>
    public static (uint Id, string Name) Member(PartyService parties, uint callerId, string name) =>
        parties.PartyOf(callerId)?.FindByName(name) is { } member
            ? (member.Id.Value, member.Name)
            : (NoCharacter, name);
}

/// <summary>/invite &lt;name&gt; (alias /inv): the same request as CMSG_PARTY_INVITE.</summary>
public sealed class InviteCommand(PartyService parties) : ICommand
{
    public string Name => "invite";
    public string[] Aliases => ["inv"];

    public void Execute(CommandContext ctx, string[] args)
    {
        if (!PartyCommandReply.TryCharacter(ctx, out uint id))
            return;
        if (args.Length != 1)
        {
            ctx.Reply("Usage: /invite <name>");
            return;
        }

        PartyResult result = parties.Invite(id, args[0]);
        string name = parties.OnlineConnectionByName(args[0])?.Character?.Name ?? args[0];
        PartyCommandReply.Send(ctx, result, name);
        if (result == PartyResult.Ok)
            ctx.Reply($"You invited {name} to the party.");
    }
}

/// <summary>/leave: the same request as CMSG_PARTY_LEAVE.</summary>
public sealed class LeaveCommand(PartyService parties) : ICommand
{
    public string Name => "leave";
    public string[] Aliases => [];

    public void Execute(CommandContext ctx, string[] args)
    {
        if (PartyCommandReply.TryCharacter(ctx, out uint id))
            PartyCommandReply.Send(ctx, parties.Leave(id), null);
    }
}

/// <summary>/kick &lt;name&gt;: the same request as CMSG_PARTY_KICK, naming the member instead of its id.</summary>
public sealed class KickCommand(PartyService parties) : ICommand
{
    public string Name => "kick";
    public string[] Aliases => [];

    public void Execute(CommandContext ctx, string[] args)
    {
        if (!PartyCommandReply.TryCharacter(ctx, out uint id))
            return;
        if (args.Length != 1)
        {
            ctx.Reply("Usage: /kick <name>");
            return;
        }

        (uint target, string name) = PartyCommandReply.Member(parties, id, args[0]);
        PartyCommandReply.Send(ctx, parties.Kick(id, target), name);
    }
}

/// <summary>/promote &lt;name&gt;: the same request as CMSG_PARTY_PROMOTE, naming the member instead of its id.</summary>
public sealed class PromoteCommand(PartyService parties) : ICommand
{
    public string Name => "promote";
    public string[] Aliases => [];

    public void Execute(CommandContext ctx, string[] args)
    {
        if (!PartyCommandReply.TryCharacter(ctx, out uint id))
            return;
        if (args.Length != 1)
        {
            ctx.Reply("Usage: /promote <name>");
            return;
        }

        (uint target, string name) = PartyCommandReply.Member(parties, id, args[0]);
        PartyCommandReply.Send(ctx, parties.Promote(id, target), name);
    }
}

/// <summary>/partyxp even|level: the same request as CMSG_PARTY_EXPERIENCE_MODE. Anything else is answered with its usage only.</summary>
public sealed class PartyExperienceCommand(PartyService parties) : ICommand
{
    public string Name => "partyxp";
    public string[] Aliases => [];

    public void Execute(CommandContext ctx, string[] args)
    {
        if (!PartyCommandReply.TryCharacter(ctx, out uint id))
            return;

        PartyExperienceMode mode = args.Length == 1 ? args[0].ToLowerInvariant() switch
        {
            "even" => PartyExperienceMode.Even,
            "level" => PartyExperienceMode.LevelWeighted,
            _ => PartyExperienceMode.Unknown,
        } : PartyExperienceMode.Unknown;

        if (mode == PartyExperienceMode.Unknown)
        {
            ctx.Reply("Usage: /partyxp <even|level>");
            return;
        }

        PartyCommandReply.Send(ctx, parties.SetExperienceMode(id, mode), null);
    }
}

/// <summary>/p (alias /party): to every online member of the sender's party, wherever they are, on the party channel; the sender sees it too.</summary>
public sealed class PartyChatCommand(PartyService parties) : ICommand
{
    public string Name => "p";
    public string[] Aliases => ["party"];

    public void Execute(CommandContext ctx, string[] args)
    {
        if (ctx.Connection.Character is not { } sender)
            return;

        string message = CommandText.AfterCommandWord(ctx.Packet.Message);
        if (message.Length == 0)
        {
            ctx.Reply("Usage: /p <message>");
            return;
        }

        if (parties.PartyOf(sender.Guid.Id) is not { } party)
        {
            ctx.Reply(PartyReplies.Describe(PartyResult.NotInParty, null)!);
            return;
        }

        ulong accountId = ctx.Connection.AccountId is { } account ? (ulong)account.Value : 0UL;
        foreach (PartyMember member in party.Members)
        {
            if (parties.OnlineConnection(member.Id.Value) is { } target)
            {
                target.Send(SChatMessagePacket.Create(accountId, sender.Guid.Id, sender.Name, message, ctx.Packet.DateTime,
                    target.CryptoSession.Encrypt, ChatChannel.Party));
            }
        }
    }
}
