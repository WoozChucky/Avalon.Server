using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Social;
using Avalon.Server.World.UnitTests.Parties;
using Avalon.World.Chat;
using Avalon.World.Public.Characters;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Chat;

/// <summary>/w &lt;player&gt; &lt;message&gt; (#717): one online character, wherever it is, on the whisper channel.</summary>
public class WhisperCommandShould
{
    private readonly PartyTestWorld _w = new();

    private void Run(PartyClient client, string message)
    {
        string[] parts = message.TrimStart('/').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        new WhisperCommand(_w.Parties.Online, ChatLimits.Off()).Execute(
            new CommandContext(client.Connection, new CChatMessagePacket { Message = message, DateTime = DateTime.UtcNow }, e => throw e),
            parts[1..]);
    }

    private static List<SChatMessagePacket> Whispers(PartyClient client) =>
        client.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE).Where(m => m.Channel == ChatChannel.Whisper).ToList();

    [Fact]
    public void Deliver_to_the_target_in_another_instance_with_the_senders_name()
    {
        PartyClient a = _w.Online(1, "Aren", instance: Guid.NewGuid());
        PartyClient b = _w.Online(2, "Kaela", instance: Guid.NewGuid());
        PartyClient stranger = _w.Online(3, "Tom", instance: b.Character.InstanceId);

        Run(a, "/w Kaela meet me in town");

        SChatMessagePacket heard = Assert.Single(Whispers(b));
        Assert.Equal("meet me in town", heard.Message);
        Assert.Equal("Aren", heard.CharacterName);
        Assert.Equal(1UL, heard.CharacterId);
        Assert.Null(heard.TargetName);
        Assert.Empty(stranger.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE));
    }

    [Fact]
    public void Echo_the_line_to_the_sender_with_the_targets_name()
    {
        PartyClient a = _w.Online(1, "Aren");
        _w.Online(2, "Kaela");

        Run(a, "/whisper kAELA hi");

        SChatMessagePacket echo = Assert.Single(Whispers(a));
        Assert.Equal("hi", echo.Message);
        Assert.Equal("Aren", echo.CharacterName);
        Assert.Equal("Kaela", echo.TargetName); // the target's own spelling, not as typed
        Assert.Empty(a.Lines());
    }

    [Theory]
    [InlineData("/w kaela hello")]
    [InlineData("/w KAELA hello")]
    [InlineData("/w   Kaela    hello")]
    [InlineData("  //w Kaela hello")]
    public void Find_the_target_ignoring_case_and_spaces(string message)
    {
        PartyClient a = _w.Online(1, "Aren");
        PartyClient b = _w.Online(2, "Kaela");

        Run(a, message);

        Assert.Equal("hello", Assert.Single(Whispers(b)).Message);
    }

    [Theory]
    [InlineData("/w Kaela wait   for  me ", "wait   for  me")]
    [InlineData("/w Kaela \t two\tparts", "two\tparts")]
    public void Keep_the_message_as_typed(string message, string expected)
    {
        PartyClient a = _w.Online(1, "Aren");
        PartyClient b = _w.Online(2, "Kaela");

        Run(a, message);

        Assert.Equal(expected, Assert.Single(Whispers(b)).Message);
        Assert.Equal(expected, Assert.Single(Whispers(a)).Message);
    }

    [Theory]
    [InlineData("/w")]
    [InlineData("/w   ")]
    [InlineData("/w Kaela")]
    [InlineData("/w Kaela    ")]
    [InlineData("/whisper \t ")]
    public void Answer_usage_without_a_name_or_a_message(string message)
    {
        PartyClient a = _w.Online(1, "Aren");
        PartyClient b = _w.Online(2, "Kaela");

        Run(a, message);

        Assert.Equal([WhisperCommand.Usage], a.Lines());
        Assert.Empty(Whispers(a));
        Assert.Empty(b.Sent);
    }

    [Theory]
    [InlineData("/w Aren hello")]
    [InlineData("/w aREN hello")]
    public void Refuse_a_whisper_to_yourself(string message)
    {
        PartyClient a = _w.Online(1, "Aren");

        Run(a, message);

        Assert.Equal(["You can't whisper yourself."], a.Lines());
        Assert.Empty(Whispers(a));
    }

    [Fact]
    public void Answer_usage_before_yourself()
    {
        PartyClient a = _w.Online(1, "Aren");

        Run(a, "/w Aren");

        Assert.Equal([WhisperCommand.Usage], a.Lines());
    }

    [Fact]
    public void Answer_an_offline_and_an_unknown_name_alike()
    {
        PartyClient a = _w.Online(1, "Aren");
        PartyClient b = _w.Online(2, "Kaela");
        _w.Parties.CharacterOffline(b.Connection, b.Character);
        b.Clear();

        Run(a, "/w kaela hello");
        Run(a, "/w Nobody hello");

        Assert.Equal(["No player named kaela is online.", "No player named Nobody is online."], a.Lines());
        Assert.Empty(Whispers(a));
        Assert.Empty(b.Sent);
    }

    [Fact]
    public void Say_nothing_for_a_connection_with_no_character()
    {
        PartyClient a = _w.Online(1, "Aren");
        PartyClient b = _w.Online(2, "Kaela");
        a.Connection.Character.Returns((ICharacter?)null);

        Run(a, "/w Kaela hello");

        Assert.Empty(a.Sent);
        Assert.Empty(b.Sent);
    }

    [Fact]
    public void Never_block_the_tick() => Assert.Empty(TickBlockingScan.Violations(typeof(WhisperCommand)));
}
