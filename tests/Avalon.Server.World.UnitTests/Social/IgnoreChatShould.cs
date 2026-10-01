using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Party;
using Avalon.Network.Packets.Social;
using Avalon.Server.World.UnitTests.Chat;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Parties;
using Avalon.World;
using Avalon.World.Chat;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Parties;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Social;

/// <summary>
/// An ignored character's chat is hidden from the one ignoring it (#723): whispers, said lines and party lines, and its
/// party invites are dropped. The ignored character is never told: everything looks delivered to it.
/// </summary>
public class IgnoreChatShould
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly PartyTestWorld _w = new();

    private static void Run(ICommand command, PartyClient client, string message)
    {
        string[] parts = message.TrimStart('/').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        command.Execute(new CommandContext(client.Connection, new CChatMessagePacket { Message = message, DateTime = Now },
            e => throw e), parts[1..]);
    }

    private static List<SChatMessagePacket> Chat(PartyClient client, ChatChannel channel) =>
        client.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE).Where(m => m.Channel == channel).ToList();

    [Fact]
    public void Not_deliver_a_whisper_from_an_ignored_character_and_echo_it_as_usual()
    {
        PartyClient aren = _w.Online(1, "Aren");
        PartyClient kaela = _w.Online(2, "Kaela");
        kaela.Character.Ignores.Add(aren.Id, "Aren", Now);

        Run(new WhisperCommand(_w.Parties.Online, ChatLimits.Off()), aren, "/w Kaela hello");

        Assert.Empty(kaela.Sent);
        SChatMessagePacket echo = Assert.Single(Chat(aren, ChatChannel.Whisper));
        Assert.Equal("hello", echo.Message);
        Assert.Equal("Kaela", echo.TargetName);
        Assert.Empty(aren.Lines());
    }

    [Fact]
    public void Deliver_a_whisper_to_a_character_you_ignore()
    {
        PartyClient aren = _w.Online(1, "Aren");
        PartyClient kaela = _w.Online(2, "Kaela");
        aren.Character.Ignores.Add(kaela.Id, "Kaela", Now);

        Run(new WhisperCommand(_w.Parties.Online, ChatLimits.Off()), aren, "/w Kaela hello");

        Assert.Equal("hello", Assert.Single(Chat(kaela, ChatChannel.Whisper)).Message);
    }

    [Fact]
    public void Leave_a_party_line_out_for_each_member_ignoring_the_sender()
    {
        PartyClient aren = _w.Online(1, "Aren");
        PartyClient kaela = _w.Online(2, "Kaela");
        PartyClient tom = _w.Online(3, "Tom");
        _w.Form(aren, kaela, tom);
        kaela.Character.Ignores.Add(aren.Id, "Aren", Now);

        Run(new PartyChatCommand(_w.Parties, ChatLimits.Off()), aren, "/p pull");

        Assert.Empty(Chat(kaela, ChatChannel.Party));
        Assert.Single(Chat(tom, ChatChannel.Party));
        Assert.Single(Chat(aren, ChatChannel.Party)); // the sender's own line, as always
    }

    [Fact]
    public void Leave_a_said_line_out_for_each_listener_ignoring_the_sender()
    {
        IWorld world = MapInstanceClients.NewWorld();
        MapInstance here = TestMapInstances.Build(world);
        world.InstanceRegistry.GetInstanceById(here.InstanceId).Returns(here);
        MapInstanceClient sender = MapInstanceClients.Join(here, 1);
        MapInstanceClient ignoring = MapInstanceClients.Join(here, 2);
        MapInstanceClient other = MapInstanceClients.Join(here, 3);
        sender.Connection.AccountId.Returns(new AccountId(1));
        ignoring.Character.Ignores.Add(1, "Tester1", Now);

        new ChatMessageHandler(world, Substitute.For<ICommandDispatcher>(), ChatLimits.Off())
            .Execute(sender.Connection, new CChatMessagePacket { Message = "Hello", DateTime = Now });

        Assert.Empty(ignoring.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE));
        Assert.Single(other.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE));
        Assert.Single(sender.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE));
    }

    [Fact]
    public void Drop_an_invite_from_an_ignored_character_while_answering_it_as_sent()
    {
        PartyClient aren = _w.Online(1, "Aren");
        PartyClient kaela = _w.Online(2, "Kaela");
        kaela.Character.Ignores.Add(aren.Id, "Aren", Now);

        Assert.Equal(PartyResult.Ok, _w.Parties.Invite(aren.Id, "Kaela"));

        Assert.Empty(kaela.Sent);
        Assert.Equal(PartyResult.NoInvite, _w.Parties.Respond(kaela.Id, accept: true));
        Assert.Null(_w.Parties.PartyOf(kaela.Id));
    }

    [Fact]
    public void Answer_a_second_invite_to_an_ignoring_character_as_pending()
    {
        PartyClient aren = _w.Online(1, "Aren");
        PartyClient kaela = _w.Online(2, "Kaela");
        kaela.Character.Ignores.Add(aren.Id, "Aren", Now);
        _w.Parties.Invite(aren.Id, "Kaela");

        Assert.Equal(PartyResult.InvitePending, _w.Parties.Invite(aren.Id, "Kaela"));
    }

    [Fact]
    public void Let_another_character_invite_the_ignoring_character_meanwhile()
    {
        PartyClient aren = _w.Online(1, "Aren");
        PartyClient kaela = _w.Online(2, "Kaela");
        PartyClient tom = _w.Online(3, "Tom");
        kaela.Character.Ignores.Add(aren.Id, "Aren", Now);
        _w.Parties.Invite(aren.Id, "Kaela");

        Assert.Equal(PartyResult.Ok, _w.Parties.Invite(tom.Id, "Kaela"));

        Assert.Equal("Tom", Assert.Single(kaela.Invites()).InviterName);
    }

    [Fact]
    public void Expire_a_dropped_invite_for_the_inviter_only()
    {
        PartyClient aren = _w.Online(1, "Aren");
        PartyClient kaela = _w.Online(2, "Kaela");
        kaela.Character.Ignores.Add(aren.Id, "Aren", Now);
        _w.Parties.Invite(aren.Id, "Kaela");

        _w.Clock.Advance(TimeSpan.FromSeconds(59));
        _w.Parties.Tick();
        Assert.Empty(aren.Results());

        _w.Clock.Advance(TimeSpan.FromSeconds(1));
        _w.Parties.Tick();

        SPartyResultPacket expired = Assert.Single(aren.Results());
        Assert.Equal((PartyResult.InviteExpired, "Kaela"), (expired.Result, expired.Name));
        Assert.Empty(kaela.Sent);
        Assert.Equal(PartyResult.Ok, _w.Parties.Invite(aren.Id, "Kaela")); // spent: a new one may be sent
    }

    [Fact]
    public void End_a_dropped_invite_when_its_target_goes_offline_as_any_other()
    {
        PartyClient aren = _w.Online(1, "Aren");
        PartyClient kaela = _w.Online(2, "Kaela");
        kaela.Character.Ignores.Add(aren.Id, "Aren", Now);
        _w.Parties.Invite(aren.Id, "Kaela");

        _w.Parties.CharacterOffline(kaela.Connection, kaela.Character);

        Assert.Equal(PartyResult.InviteExpired, Assert.Single(aren.Results()).Result);
    }

    [Fact]
    public void End_a_dropped_invite_silently_when_its_inviter_goes_offline()
    {
        PartyClient aren = _w.Online(1, "Aren");
        PartyClient kaela = _w.Online(2, "Kaela");
        kaela.Character.Ignores.Add(aren.Id, "Aren", Now);
        _w.Parties.Invite(aren.Id, "Kaela");

        _w.Parties.CharacterOffline(aren.Connection, aren.Character);
        _w.Clock.Advance(TimeSpan.FromSeconds(61));
        _w.Parties.Tick();

        Assert.Empty(kaela.Sent);
        Assert.Empty(aren.Results());
    }

    [Fact]
    public void Refuse_an_invite_to_an_ignoring_character_in_a_party_as_any_other()
    {
        PartyClient aren = _w.Online(1, "Aren");
        PartyClient kaela = _w.Online(2, "Kaela");
        PartyClient tom = _w.Online(3, "Tom");
        _w.Form(tom, kaela);
        kaela.Character.Ignores.Add(aren.Id, "Aren", Now);

        Assert.Equal(PartyResult.AlreadyInParty, _w.Parties.Invite(aren.Id, "Kaela"));
    }
}
