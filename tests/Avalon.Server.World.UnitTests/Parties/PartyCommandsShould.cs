using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Party;
using Avalon.Network.Packets.Social;
using Avalon.Server.World.UnitTests.Chat;
using Avalon.World.Chat;
using Avalon.World.Handlers;
using Avalon.World.Parties;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Parties;

public class PartyCommandsShould
{
    private readonly PartyTestWorld _w = new();

    private static void Run(ICommand command, PartyClient client, string message)
    {
        string[] parts = message.TrimStart('/').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        command.Execute(new CommandContext(client.Connection, new CChatMessagePacket { Message = message, DateTime = DateTime.UtcNow },
            e => throw e), parts[1..]);
    }

    [Fact]
    public void Invite_by_name_and_answer_with_the_result()
    {
        PartyClient a = _w.Online(1, "A");
        PartyClient b = _w.Online(2, "Kaela");

        Run(new InviteCommand(_w.Parties), a, "/invite kaela");

        Assert.Equal(PartyResult.Ok, Assert.Single(a.Results()).Result);
        Assert.Single(b.Invites());
        Assert.Contains("You invited Kaela to the party.", a.Lines());
    }

    /// <summary>#936: as the packet, /invite refuses a name that is not a character name before anything is looked up.</summary>
    [Fact]
    public void Refuse_an_invite_that_names_no_character_before_looking_anyone_up()
    {
        PartyClient a = _w.Online(1, "Aren");
        PartyClient b = _w.Online(2, "Tester2"); // a digit, so not a name, though a character here holds it

        Run(new InviteCommand(_w.Parties), a, "/invite Tester2");

        Assert.Equal(PartyResult.NotFound, Assert.Single(a.Results()).Result);
        Assert.Equal(["Could not find Tester2."], a.Lines());
        Assert.Empty(b.Invites());
    }

    /// <summary>
    /// #944: the invite packet and /invite share one budget per inviter, <c>Game:PartyInvitesPerMinute</c> in any 60 s.
    /// Every invite that looks its target up spends one, whatever it finds; one that names no character does not. Past
    /// it an invite is answered TooFast with the name as typed (and a line, on /invite): nothing is looked up and nobody
    /// is invited, until the oldest leaves the window. Refused before the lookup, /invite names the target as typed.
    /// </summary>
    [Fact]
    public void Refuse_invites_past_the_budget_the_packet_and_the_command_share_looking_nothing_up()
    {
        var w = new PartyTestWorld(c => c.PartyInvitesPerMinute = 3);
        PartyClient aren = w.Online(1, "Aren");
        PartyClient kaela = w.Online(2, "Kaela");
        var packet = new PartyInviteHandler(w.Parties, NullLogger<PartyInviteHandler>.Instance);
        var command = new InviteCommand(w.Parties);

        packet.Execute(aren.Connection, new CPartyInvitePacket { TargetName = "Nobody" });   // NotFound: spends one
        packet.Execute(aren.Connection, new CPartyInvitePacket { TargetName = "Tester9" });  // not a name: spends none
        Run(command, aren, "/invite Aren");                                                // Self: spends one
        w.Clock.Advance(TimeSpan.FromSeconds(10));
        Run(command, aren, "/invite Nobody");                                              // NotFound: spends the last
        aren.Clear();

        packet.Execute(aren.Connection, new CPartyInvitePacket { TargetName = "Kaela" });
        Run(command, aren, "/invite kaela");

        Assert.Equal([(PartyResult.TooFast, "Kaela"), (PartyResult.TooFast, "kaela")],
            aren.Results().Select(r => (r.Result, r.Name)));
        Assert.Equal(["You're sending party invites too fast. Try again in a moment."], aren.Lines());
        Assert.Empty(kaela.Invites());

        // The first two leave the window: the next invite is looked up and sent.
        w.Clock.Advance(TimeSpan.FromSeconds(50));
        aren.Clear();
        Run(command, aren, "/invite kaela");
        Assert.Equal((PartyResult.Ok, "Kaela"), (Assert.Single(aren.Results()).Result, aren.Results()[0].Name));
        Assert.Single(kaela.Invites());

        // Refused before the lookup, a member who does not lead is answered with the name as typed, never as spelled.
        PartyClient tom = w.Online(3, "Tom");
        Assert.Equal(PartyResult.Ok, w.Parties.Respond(kaela.Id, accept: true));
        Run(command, kaela, "/invite tom");
        Assert.Equal((PartyResult.NotLeader, "tom"), (Assert.Single(kaela.Results()).Result, kaela.Results()[0].Name));
        Assert.Empty(tom.Invites());
    }

    [Fact]
    public void Kick_and_promote_by_member_name()
    {
        PartyClient a = _w.Online(1, "A");
        PartyClient b = _w.Online(2, "B");
        PartyClient c = _w.Online(3, "C");
        _w.Form(a, b, c);

        Run(new PromoteCommand(_w.Parties), a, "/promote b");
        Run(new KickCommand(_w.Parties), b, "/kick C");

        Assert.True(_w.Parties.PartyOf(b.Id)!.IsLeader(b.Id));
        Assert.Null(_w.Parties.PartyOf(c.Id));
    }

    [Fact]
    public void Refuse_a_kick_from_a_member_who_does_not_lead_before_looking_up_the_name()
    {
        PartyClient a = _w.Online(1, "A");
        PartyClient b = _w.Online(2, "B");
        _w.Form(a, b);

        Run(new KickCommand(_w.Parties), b, "/kick nobody");

        Assert.Equal(PartyResult.NotLeader, Assert.Single(b.Results()).Result);
        Assert.Equal(["Only the party leader can do that."], b.Lines());
    }

    [Theory]
    [InlineData("/partyxp even", PartyExperienceMode.Even)]
    [InlineData("/partyxp level", PartyExperienceMode.LevelWeighted)]
    public void Set_the_experience_mode(string message, PartyExperienceMode expected)
    {
        PartyClient a = _w.Online(1, "A");
        PartyClient b = _w.Online(2, "B");
        _w.Form(a, b);

        Run(new PartyExperienceCommand(_w.Parties), a, message);

        Assert.Equal(expected, _w.Parties.PartyOf(a.Id)!.ExperienceMode);
    }

    [Fact]
    public void Answer_usage_for_an_unknown_mode()
    {
        PartyClient a = _w.Online(1, "A");

        Run(new PartyExperienceCommand(_w.Parties), a, "/partyxp most");

        Assert.Equal(["Usage: /partyxp <even|level>"], a.Lines());
        Assert.Empty(a.Results());
    }

    [Fact]
    public void Send_party_chat_to_every_online_member_on_the_party_channel()
    {
        PartyClient a = _w.Online(1, "A", instance: Guid.NewGuid());
        PartyClient b = _w.Online(2, "B", instance: Guid.NewGuid()); // another instance: still hears it
        PartyClient stranger = _w.Online(3, "C");
        _w.Form(a, b);

        Run(new PartyChatCommand(_w.Parties, ChatLimits.Off()), a, "/p pull the big one");

        SChatMessagePacket heard = Assert.Single(b.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE));
        Assert.Equal(ChatChannel.Party, heard.Channel);
        Assert.Equal("pull the big one", heard.Message);
        Assert.Equal("A", heard.CharacterName);
        Assert.Single(a.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE)); // echo
        Assert.Empty(stranger.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE));
    }

    [Fact]
    public void Tell_a_player_with_no_party_that_party_chat_goes_nowhere()
    {
        PartyClient a = _w.Online(1, "A");

        Run(new PartyChatCommand(_w.Parties, ChatLimits.Off()), a, "/p hello");

        Assert.Equal(["You are not in a party."], a.Lines());
    }

    [Theory]
    [InlineData("/p")]
    [InlineData("/party 	 ")]
    public void Answer_usage_for_party_chat_with_no_text(string message)
    {
        PartyClient a = _w.Online(1, "A");
        PartyClient b = _w.Online(2, "B");
        _w.Form(a, b);
        a.Clear();

        Run(new PartyChatCommand(_w.Parties, ChatLimits.Off()), a, message);

        Assert.Equal(["Usage: /p <message>"], a.Lines());
        Assert.DoesNotContain(b.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE), m => m.Channel == ChatChannel.Party);
    }

    /// <summary>
    /// The dispatcher skips spaces and slashes before the command word, so the text must be found the same way, and it
    /// is kept as typed: the dispatcher's arguments would collapse repeated spaces.
    /// </summary>
    [Theory]
    [InlineData("/party wait   for me", "wait   for me")]
    [InlineData("/ p hello", "hello")]
    [InlineData("//p  hello there ", "hello there")]
    [InlineData("/p	hello", "hello")]
    public void Find_the_party_chat_text_after_the_command_word(string message, string expected)
    {
        PartyClient a = _w.Online(1, "A");
        PartyClient b = _w.Online(2, "B");
        _w.Form(a, b);

        Run(new PartyChatCommand(_w.Parties, ChatLimits.Off()), a, message);

        SChatMessagePacket heard = Assert.Single(b.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE),
            m => m.Channel == ChatChannel.Party);
        Assert.Equal(expected, heard.Message);
    }

    /// <summary>#763: party chat carries the sender's class, to the members and on the echo.</summary>
    [Fact]
    public void Carry_the_senders_class_on_party_chat()
    {
        PartyClient a = _w.Online(1, "A");
        PartyClient b = _w.Online(2, "B");
        a.Character.Data!.Class = CharacterClass.Healer;
        _w.Form(a, b);

        Run(new PartyChatCommand(_w.Parties, ChatLimits.Off()), a, "/p heal up");

        Assert.Equal(CharacterClass.Healer,
            Assert.Single(b.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE)).CharacterClass);
        Assert.Equal(CharacterClass.Healer,
            Assert.Single(a.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE)).CharacterClass);
    }
}
