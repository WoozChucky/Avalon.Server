using Avalon.Common;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Social;
using Avalon.Server.World.UnitTests.Chat;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Parties;
using Avalon.World;
using Avalon.World.Chat;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Public;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.World.UnitTests.Handlers;

public class ChatMessageHandlerShould
{
    private const string TooLong = "Your message is too long and was not sent.";

    private readonly IWorld _world = MapInstanceClients.NewWorld();
    private readonly ICommandDispatcher _commandDispatcher = Substitute.For<ICommandDispatcher>();
    private readonly IWorldConnection _senderConnection = Substitute.For<IWorldConnection>();
    private readonly ChatMessageHandler _handler;

    public ChatMessageHandlerShould()
    {
        _senderConnection.AccountId.Returns(new AccountId(1));
        _senderConnection.InGame.Returns(true);

        _handler = new ChatMessageHandler(_world, _commandDispatcher, ChatLimits.Off());
    }

    private CChatMessagePacket MakePacket(string message) =>
        new() { Message = message, DateTime = DateTime.UtcNow };

    [Fact]
    public void Say_nothing_aloud_for_a_command()
    {
        _commandDispatcher.Dispatch(Arg.Any<IWorldConnection>(), Arg.Any<CChatMessagePacket>()).Returns(true);
        MapInstance here = TestMapInstances.Build(_world);
        _world.InstanceRegistry.GetInstanceById(here.InstanceId).Returns(here);
        MapInstanceClient sender = MapInstanceClients.Join(here, 1);
        MapInstanceClient neighbour = MapInstanceClients.Join(here, 2);

        _handler.Execute(sender.Connection, MakePacket("/invite PlayerOne"));

        Assert.Empty(neighbour.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE));
    }

    /// <summary>A said line reaches everyone in the sender's instance and the sender's echo, carrying the sender's class (#763).</summary>
    [Fact]
    public void Send_a_plain_message_to_everyone_in_the_senders_instance_only()
    {
        MapInstance here = TestMapInstances.Build(_world);
        MapInstance elsewhere = TestMapInstances.Build(_world);
        _world.InstanceRegistry.GetInstanceById(here.InstanceId).Returns(here);
        _world.InstanceRegistry.GetInstanceById(elsewhere.InstanceId).Returns(elsewhere);
        CharacterEntity hunter = Inventory.TestCharacters.New(1);
        hunter.Data!.Class = CharacterClass.Hunter;
        MapInstanceClient sender = MapInstanceClients.Join(here, hunter);
        MapInstanceClient neighbour = MapInstanceClients.Join(here, 2);
        MapInstanceClient stranger = MapInstanceClients.Join(elsewhere, 3);
        sender.Connection.AccountId.Returns(new AccountId(1));

        _handler.Execute(sender.Connection, MakePacket("Hello"));

        SChatMessagePacket heard = Assert.Single(neighbour.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE));
        Assert.Equal(("Hello", ChatChannel.Say, (ushort)CharacterClass.Hunter), (heard.Message, heard.Channel, heard.CharacterClass));
        SChatMessagePacket echo = Assert.Single(sender.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE));
        Assert.Equal((ushort)CharacterClass.Hunter, echo.CharacterClass);
        Assert.Empty(stranger.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE));
    }

    /// <summary>The answer is a System line, which no character sent, so it carries class 0 (#763).</summary>
    [Fact]
    public void Answer_an_unknown_command_on_the_system_channel()
    {
        _commandDispatcher.Dispatch(Arg.Any<IWorldConnection>(), Arg.Any<CChatMessagePacket>()).Returns(false);
        var sent = new List<OutboundPacket>();
        _senderConnection.When(c => c.Send(Arg.Any<OutboundPacket>())).Do(ci => sent.Add(ci.Arg<OutboundPacket>()));

        _handler.Execute(_senderConnection, MakePacket("/nope"));

        using var stream = new MemoryStream(Assert.Single(sent).PayloadMemory.ToArray());
        SChatMessagePacket reply = Serializer.Deserialize<SChatMessagePacket>(stream);
        Assert.Equal((ChatChannel.System, "Unknown command.", (ushort)0), (reply.Channel, reply.Message, reply.CharacterClass));
    }

    /// <summary>#763: a said line from a sender whose instance cannot be found still carries its class.</summary>
    [Fact]
    public void Carry_the_senders_class_when_only_the_sender_hears_it()
    {
        var sent = new List<OutboundPacket>();
        _senderConnection.When(c => c.Send(Arg.Any<OutboundPacket>())).Do(ci => sent.Add(ci.Arg<OutboundPacket>()));
        CharacterEntity wizard = Inventory.TestCharacters.New(1);
        wizard.Data!.Class = CharacterClass.Wizard;
        _senderConnection.Character.Returns(wizard);

        _handler.Execute(_senderConnection, MakePacket("Hello"));

        using var stream = new MemoryStream(Assert.Single(sent).PayloadMemory.ToArray());
        Assert.Equal((ushort)CharacterClass.Wizard, Serializer.Deserialize<SChatMessagePacket>(stream).CharacterClass);
    }

    /// <summary>
    /// The length rule (#936): the whole message as sent is at most 511 bytes of UTF-8, which is not its number of
    /// characters. A longer one is said to nobody, the sender alone is told, and it uses none of the rate limit.
    /// </summary>
    [Theory]
    [InlineData(0x61, 511, 0, true)]
    [InlineData(0x61, 512, 0, false)]
    [InlineData(0x20AC, 170, 1, true)]    // three bytes each, and one letter: 171 characters, 511 bytes
    [InlineData(0x20AC, 171, 0, false)]   // 171 characters, 513 bytes
    [InlineData(0x1F600, 127, 3, true)]   // four bytes each, in two UTF-16 units: 511 bytes
    [InlineData(0x1F600, 128, 0, false)]  // 512 bytes
    [InlineData(0xD800, 171, 0, false)]   // half a surrogate pair, 171 times: malformed, counted as it would be sent on, 513 bytes
    public void Say_a_message_of_511_bytes_and_refuse_a_longer_one_telling_only_the_sender(int codePoint, int times,
        int letters, bool said)
    {
        string unit = codePoint > 0xFFFF ? char.ConvertFromUtf32(codePoint) : ((char)codePoint).ToString();
        string message = string.Concat(Enumerable.Repeat(unit, times)) + new string('z', letters);
        var limiter = new ChatRateLimiter(Options.Create(new GameConfiguration { ChatMessagesPerMinute = 1 }), TimeProvider.System);
        var handler = new ChatMessageHandler(_world, _commandDispatcher, limiter);
        MapInstance here = TestMapInstances.Build(_world);
        _world.InstanceRegistry.GetInstanceById(here.InstanceId).Returns(here);
        MapInstanceClient sender = MapInstanceClients.Join(here, 1);
        MapInstanceClient neighbour = MapInstanceClients.Join(here, 2);

        handler.Execute(sender.Connection, MakePacket(message));

        if (said)
        {
            Assert.Equal(message, Assert.Single(neighbour.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE)).Message);
            return;
        }

        Assert.Empty(neighbour.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE));
        SChatMessagePacket told = Assert.Single(sender.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE));
        Assert.Equal((ChatChannel.System, TooLong), (told.Channel, told.Message));

        // The budget is one message and the refusal used none of it: the next message is said.
        handler.Execute(sender.Connection, MakePacket("after"));
        Assert.Equal("after", Assert.Single(neighbour.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE)).Message);
    }

    /// <summary>
    /// The rule is on the message as sent (#936), so a command is held to it: an over-long whisper reaches neither the
    /// command nor its target.
    /// </summary>
    [Fact]
    public void Refuse_an_over_long_whisper_before_the_command_runs()
    {
        const string Prefix = "/w Kaela ";
        var online = new PartyTestWorld();
        var handler = new ChatMessageHandler(_world,
            new CommandDispatcher([new WhisperCommand(online.Parties.Online, ChatLimits.Off())], NullLogger<CommandDispatcher>.Instance),
            ChatLimits.Off());
        PartyClient aren = online.Online(1, "Aren");
        PartyClient kaela = online.Online(2, "Kaela");
        aren.Connection.AccessLevel.Returns(AccessLevels.Player);

        handler.Execute(aren.Connection, MakePacket(Prefix + new string('x', 511 - Prefix.Length)));
        handler.Execute(aren.Connection, MakePacket(Prefix + new string('y', 512 - Prefix.Length)));

        SChatMessagePacket heard = Assert.Single(kaela.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE));
        Assert.Equal(new string('x', 511 - Prefix.Length), heard.Message);
        Assert.Equal([TooLong], aren.Lines());
    }
}
