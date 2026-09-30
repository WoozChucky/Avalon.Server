using System.IO;
using Avalon.Common.Cryptography;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Social;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World;
using Avalon.World.Chat;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.World.UnitTests.Handlers;

public class ChatMessageHandlerShould
{
    private readonly IWorld _world = MapInstanceClients.NewWorld();
    private readonly ICommandDispatcher _commandDispatcher = Substitute.For<ICommandDispatcher>();
    private readonly IWorldConnection _senderConnection = Substitute.For<IWorldConnection>();
    private readonly IAvalonCryptoSession _senderCrypto = new FakeAvalonCryptoSession();
    private readonly ChatMessageHandler _handler;

    public ChatMessageHandlerShould()
    {
        _senderConnection.CryptoSession.Returns(_senderCrypto);
        _senderConnection.AccountId.Returns(new AccountId(1));
        _senderConnection.InGame.Returns(true);

        _handler = new ChatMessageHandler(_world, _commandDispatcher);
    }

    private CChatMessagePacket MakePacket(string message) =>
        new() { Message = message, DateTime = DateTime.UtcNow };

    [Fact]
    public void Dispatch_SlashCommand_To_CommandDispatcher()
    {
        _commandDispatcher.Dispatch(Arg.Any<IWorldConnection>(), Arg.Any<CChatMessagePacket>())
            .Returns(true);

        _handler.Execute(_senderConnection, MakePacket("/invite PlayerOne"));

        _commandDispatcher.Received(1).Dispatch(_senderConnection, Arg.Any<CChatMessagePacket>());
    }

    [Fact]
    public void Send_System_Error_When_Command_Not_Found()
    {
        _commandDispatcher.Dispatch(Arg.Any<IWorldConnection>(), Arg.Any<CChatMessagePacket>())
            .Returns(false);

        _handler.Execute(_senderConnection, MakePacket("/unknown"));

        _senderConnection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

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

    [Fact]
    public void Send_a_plain_message_to_everyone_in_the_senders_instance_only()
    {
        MapInstance here = TestMapInstances.Build(_world);
        MapInstance elsewhere = TestMapInstances.Build(_world);
        _world.InstanceRegistry.GetInstanceById(here.InstanceId).Returns(here);
        _world.InstanceRegistry.GetInstanceById(elsewhere.InstanceId).Returns(elsewhere);
        MapInstanceClient sender = MapInstanceClients.Join(here, 1);
        MapInstanceClient neighbour = MapInstanceClients.Join(here, 2);
        MapInstanceClient stranger = MapInstanceClients.Join(elsewhere, 3);
        sender.Connection.AccountId.Returns(new AccountId(1));

        _handler.Execute(sender.Connection, MakePacket("Hello"));

        SChatMessagePacket heard = Assert.Single(neighbour.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE));
        Assert.Equal("Hello", heard.Message);
        Assert.Equal(ChatChannel.Say, heard.Channel);
        Assert.Single(sender.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE)); // the echo
        Assert.Empty(stranger.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE));
    }

    [Fact]
    public void Send_nothing_for_a_sender_with_no_character()
    {
        _senderConnection.Character.Returns((ICharacter?)null);

        _handler.Execute(_senderConnection, MakePacket("Hello"));

        _senderConnection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Answer_an_unknown_command_on_the_system_channel()
    {
        _commandDispatcher.Dispatch(Arg.Any<IWorldConnection>(), Arg.Any<CChatMessagePacket>()).Returns(false);
        var sent = new List<NetworkPacket>();
        _senderConnection.When(c => c.Send(Arg.Any<NetworkPacket>())).Do(ci => sent.Add(ci.Arg<NetworkPacket>()));

        _handler.Execute(_senderConnection, MakePacket("/nope"));

        using var stream = new MemoryStream(Assert.Single(sent).Payload);
        SChatMessagePacket reply = Serializer.Deserialize<SChatMessagePacket>(stream);
        Assert.Equal(ChatChannel.System, reply.Channel);
        Assert.Equal("Unknown command.", reply.Message);
    }
}
