using System.Collections.Immutable;
using Avalon.Common;
using Avalon.Common.Cryptography;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Social;
using Avalon.Server.World.Handlers;
using Avalon.World;
using Avalon.World.Chat;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Handlers;

public class ChatMessageHandlerShould
{
    private readonly IWorldServer _worldServer = Substitute.For<IWorldServer>();
    private readonly ICommandDispatcher _commandDispatcher = Substitute.For<ICommandDispatcher>();
    private readonly IWorldConnection _senderConnection = Substitute.For<IWorldConnection>();
    private readonly IAvalonCryptoSession _senderCrypto = Substitute.For<IAvalonCryptoSession>();
    private readonly ChatMessageHandler _handler;

    public ChatMessageHandlerShould()
    {
        _senderCrypto.Encrypt(Arg.Any<byte[]>()).Returns(x => (byte[])x[0]);
        _senderConnection.CryptoSession.Returns(_senderCrypto);
        _senderConnection.AccountId.Returns(new AccountId(1));
        _senderConnection.InGame.Returns(true);

        _worldServer.Connections.Returns(ImmutableArray<IWorldConnection>.Empty);
        _handler = new ChatMessageHandler(_worldServer, _commandDispatcher);
    }

    private WorldPacketContext<CChatMessagePacket> MakeCtx(string message) =>
        new()
        {
            Packet = new CChatMessagePacket { Message = message, DateTime = DateTime.UtcNow },
            Connection = _senderConnection
        };

    [Fact]
    public async Task Dispatch_SlashCommand_To_CommandDispatcher()
    {
        _commandDispatcher.DispatchAsync(Arg.Any<WorldPacketContext<CChatMessagePacket>>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await _handler.ExecuteAsync(MakeCtx("/invite PlayerOne"));

        await _commandDispatcher.Received(1)
            .DispatchAsync(Arg.Any<WorldPacketContext<CChatMessagePacket>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Send_System_Error_When_Command_Not_Found()
    {
        _commandDispatcher.DispatchAsync(Arg.Any<WorldPacketContext<CChatMessagePacket>>(), Arg.Any<CancellationToken>())
            .Returns(false);

        await _handler.ExecuteAsync(MakeCtx("/unknown"));

        _senderConnection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task Not_Broadcast_When_Message_Is_Command()
    {
        _commandDispatcher.DispatchAsync(Arg.Any<WorldPacketContext<CChatMessagePacket>>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var otherConnection = Substitute.For<IWorldConnection>();
        otherConnection.InGame.Returns(true);
        otherConnection.AccountId.Returns(new AccountId(2));
        var otherCrypto = Substitute.For<IAvalonCryptoSession>();
        otherCrypto.Encrypt(Arg.Any<byte[]>()).Returns(x => (byte[])x[0]);
        otherConnection.CryptoSession.Returns(otherCrypto);
        _worldServer.Connections.Returns(ImmutableArray.Create(otherConnection));

        await _handler.ExecuteAsync(MakeCtx("/invite PlayerOne"));

        otherConnection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task Broadcast_Plain_Message_To_Other_InGame_Connections()
    {
        var otherConnection = Substitute.For<IWorldConnection>();
        otherConnection.InGame.Returns(true);
        otherConnection.AccountId.Returns(new AccountId(2));
        var otherCrypto = Substitute.For<IAvalonCryptoSession>();
        otherCrypto.Encrypt(Arg.Any<byte[]>()).Returns(x => (byte[])x[0]);
        otherConnection.CryptoSession.Returns(otherCrypto);

        var character = Substitute.For<ICharacter>();
        character.Name.Returns("HeroOne");
        character.Guid.Returns(new ObjectGuid(ObjectType.Character, 42));
        _senderConnection.Character.Returns(character);

        _worldServer.Connections.Returns(ImmutableArray.Create(otherConnection));

        await _handler.ExecuteAsync(MakeCtx("Hello world"));

        otherConnection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task Not_Broadcast_To_Sender()
    {
        var character = Substitute.For<ICharacter>();
        character.Name.Returns("HeroOne");
        character.Guid.Returns(new ObjectGuid(ObjectType.Character, 42));
        _senderConnection.Character.Returns(character);

        _worldServer.Connections.Returns(ImmutableArray.Create(_senderConnection));

        await _handler.ExecuteAsync(MakeCtx("Hello world"));

        _senderConnection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task Not_Broadcast_To_Connections_Not_InGame()
    {
        var offlineConnection = Substitute.For<IWorldConnection>();
        offlineConnection.InGame.Returns(false);
        offlineConnection.AccountId.Returns(new AccountId(3));

        var character = Substitute.For<ICharacter>();
        character.Name.Returns("HeroOne");
        _senderConnection.Character.Returns(character);

        _worldServer.Connections.Returns(ImmutableArray.Create(offlineConnection));

        await _handler.ExecuteAsync(MakeCtx("Hello world"));

        offlineConnection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task Not_Broadcast_When_Sender_Not_InGame()
    {
        _senderConnection.InGame.Returns(false);

        var otherConnection = Substitute.For<IWorldConnection>();
        otherConnection.InGame.Returns(true);
        otherConnection.AccountId.Returns(new AccountId(2));
        _worldServer.Connections.Returns(ImmutableArray.Create(otherConnection));

        await _handler.ExecuteAsync(MakeCtx("Hello world"));

        otherConnection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }
}
