using Avalon.World.Public.Enums;
using Avalon.Server.World.UnitTests.Chat;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Party;
using Avalon.Network.Packets.Social;
using Avalon.World.Chat;
using Avalon.World.Parties;
using Xunit;

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

    [Fact]
    public void Add_a_line_to_a_refusal()
    {
        PartyClient a = _w.Online(1, "A");

        Run(new LeaveCommand(_w.Parties), a, "/leave");

        Assert.Equal(PartyResult.NotInParty, Assert.Single(a.Results()).Result);
        Assert.Equal(["You are not in a party."], a.Lines());
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
    public void Keep_the_party_chat_text_as_typed()
    {
        PartyClient a = _w.Online(1, "A");
        PartyClient b = _w.Online(2, "B");
        _w.Form(a, b);

        Run(new PartyChatCommand(_w.Parties, ChatLimits.Off()), a, "/party wait   for me");

        Assert.Equal("wait   for me", Assert.Single(b.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE)).Message);
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
    [InlineData("/p    ")]
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

    /// <summary>The dispatcher skips spaces and slashes before the command word, so the text must be found the same way.</summary>
    [Theory]
    [InlineData("/ p hello", "hello")]
    [InlineData("  /p hello", "hello")]
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

        Assert.Equal((ushort)CharacterClass.Healer,
            Assert.Single(b.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE)).CharacterClass);
        Assert.Equal((ushort)CharacterClass.Healer,
            Assert.Single(a.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE)).CharacterClass);
    }
}
