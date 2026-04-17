using System.Collections.Immutable;
using Avalon.Common;
using Avalon.Common.Cryptography;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Social;
using Avalon.World;
using Avalon.World.Chat;
using Avalon.World.Handlers;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Handlers;

public class ChatMessageHandlerShould
{
    private readonly IWorldServer _worldServer = Substitute.For<IWorldServer>();
    private readonly ICommandDispatcher _commandDispatcher = Substitute.For<ICommandDispatcher>();
    private readonly IWorldConnection _senderConnection = Substitute.For<IWorldConnection>();
    private readonly IAvalonCryptoSession _senderCrypto = new FakeAvalonCryptoSession();
    private readonly ChatMessageHandler _handler;

    public ChatMessageHandlerShould()
    {
        _senderConnection.CryptoSession.Returns(_senderCrypto);
        _senderConnection.AccountId.Returns(new AccountId(1));
        _senderConnection.InGame.Returns(true);

        // Make AddQueryCallback invoke the callback synchronously so tests can assert results
        _senderConnection
            .When(c => c.EnqueueContinuation(Arg.Any<Task<bool>>(), Arg.Any<Action<bool>>()))
            .Do(ci =>
            {
                var task = ci.Arg<Task<bool>>();
                var callback = ci.Arg<Action<bool>>();
                task.Wait();
                callback(task.Result);
            });

        _worldServer.Connections.Returns(ImmutableArray<IWorldConnection>.Empty);
        _handler = new ChatMessageHandler(_worldServer, _commandDispatcher);
    }

    private CChatMessagePacket MakePacket(string message) =>
        new() { Message = message, DateTime = DateTime.UtcNow };

    [Fact]
    public void Dispatch_SlashCommand_To_CommandDispatcher()
    {
        _commandDispatcher.DispatchAsync(Arg.Any<WorldPacketContext<CChatMessagePacket>>(), Arg.Any<CancellationToken>())
            .Returns(true);

        _handler.Execute(_senderConnection, MakePacket("/invite PlayerOne"));

        _commandDispatcher.Received(1)
            .DispatchAsync(Arg.Any<WorldPacketContext<CChatMessagePacket>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Send_System_Error_When_Command_Not_Found()
    {
        _commandDispatcher.DispatchAsync(Arg.Any<WorldPacketContext<CChatMessagePacket>>(), Arg.Any<CancellationToken>())
            .Returns(false);

        _handler.Execute(_senderConnection, MakePacket("/unknown"));

        _senderConnection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Not_Broadcast_When_Message_Is_Command()
    {
        _commandDispatcher.DispatchAsync(Arg.Any<WorldPacketContext<CChatMessagePacket>>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var otherConnection = Substitute.For<IWorldConnection>();
        otherConnection.InGame.Returns(true);
        otherConnection.AccountId.Returns(new AccountId(2));
        otherConnection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        _worldServer.Connections.Returns(ImmutableArray.Create(otherConnection));

        _handler.Execute(_senderConnection, MakePacket("/invite PlayerOne"));

        otherConnection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Broadcast_Plain_Message_To_Other_InGame_Connections()
    {
        var otherConnection = Substitute.For<IWorldConnection>();
        otherConnection.InGame.Returns(true);
        otherConnection.AccountId.Returns(new AccountId(2));
        otherConnection.CryptoSession.Returns(new FakeAvalonCryptoSession());

        var character = Substitute.For<ICharacter>();
        character.Name.Returns("HeroOne");
        character.Guid.Returns(new ObjectGuid(ObjectType.Character, 42));
        _senderConnection.Character.Returns(character);

        _worldServer.Connections.Returns(ImmutableArray.Create(otherConnection));

        _handler.Execute(_senderConnection, MakePacket("Hello world"));

        otherConnection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Not_Broadcast_To_Sender()
    {
        var character = Substitute.For<ICharacter>();
        character.Name.Returns("HeroOne");
        character.Guid.Returns(new ObjectGuid(ObjectType.Character, 42));
        _senderConnection.Character.Returns(character);

        _worldServer.Connections.Returns(ImmutableArray.Create(_senderConnection));

        _handler.Execute(_senderConnection, MakePacket("Hello world"));

        _senderConnection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Not_Broadcast_To_Connections_Not_InGame()
    {
        var offlineConnection = Substitute.For<IWorldConnection>();
        offlineConnection.InGame.Returns(false);
        offlineConnection.AccountId.Returns(new AccountId(3));

        var character = Substitute.For<ICharacter>();
        character.Name.Returns("HeroOne");
        _senderConnection.Character.Returns(character);

        _worldServer.Connections.Returns(ImmutableArray.Create(offlineConnection));

        _handler.Execute(_senderConnection, MakePacket("Hello world"));

        offlineConnection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Not_Broadcast_When_Sender_Not_InGame()
    {
        _senderConnection.InGame.Returns(false);

        var otherConnection = Substitute.For<IWorldConnection>();
        otherConnection.InGame.Returns(true);
        otherConnection.AccountId.Returns(new AccountId(2));
        _worldServer.Connections.Returns(ImmutableArray.Create(otherConnection));

        _handler.Execute(_senderConnection, MakePacket("Hello world"));

        otherConnection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }
}
