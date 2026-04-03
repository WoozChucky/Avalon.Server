using Avalon.Common.Cryptography;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Server.Auth;
using Avalon.Server.Auth.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using AvalonWorld = Avalon.Domain.Auth.World;

namespace Avalon.Server.Auth.UnitTests.Handlers;

public class CWorldListHandlerShould
{
    private readonly IWorldRepository _worldRepository = Substitute.For<IWorldRepository>();
    private readonly IAccountRepository _accountRepository = Substitute.For<IAccountRepository>();
    private readonly IAuthConnection _connection = Substitute.For<IAuthConnection>();
    private readonly IAvalonCryptoSession _cryptoSession = Substitute.For<IAvalonCryptoSession>();
    private readonly CWorldListHandler _handler;

    public CWorldListHandlerShould()
    {
        _cryptoSession.Encrypt(Arg.Any<byte[]>()).Returns(x => (byte[])x[0]);
        _connection.CryptoSession.Returns(_cryptoSession);
        _connection.Id.Returns(Guid.NewGuid());
        _handler = new CWorldListHandler(NullLoggerFactory.Instance, _worldRepository, _accountRepository);
    }

    private static AvalonWorld MakeWorld(ushort id, AccountAccessLevel req = AccountAccessLevel.Player)
        => new AvalonWorld
        {
            Name = $"World {id}",
            Host = "localhost",
            Port = 7001,
            MinVersion = "0.0.1",
            Version = "0.0.1",
            AccessLevelRequired = req,
            Id = new WorldId(id)
        };

    private static Account MakeAccount(AccountId? id = null, AccountAccessLevel level = AccountAccessLevel.Player)
        => new Account
        {
            Username = "TESTUSER",
            Salt = new byte[16],
            Verifier = new byte[20],
            Email = "test@test.com",
            JoinDate = DateTime.UtcNow,
            AccessLevel = level,
            Id = id ?? new AccountId(1L)
        };

    [Fact]
    public async Task CloseConnection_WhenAccountNotFound()
    {
        _worldRepository.FindAllAsync().Returns(new List<AvalonWorld>());
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>()).Returns((Account?)null);
        _connection.AccountId.Returns((AccountId?)null);

        var ctx = new AuthPacketContext<CWorldListPacket>
        {
            Packet = new CWorldListPacket(),
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        _connection.Received(1).Close();
        _connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task SendWorldList_WithOnlyAccessibleWorlds()
    {
        var account = MakeAccount(level: AccountAccessLevel.Player);
        _connection.AccountId.Returns(account.Id);
        _accountRepository.FindByIdAsync(account.Id).Returns(account);

        var playerWorld = MakeWorld(1, AccountAccessLevel.Player);
        var adminWorld = MakeWorld(2, AccountAccessLevel.Administrator);
        _worldRepository.FindAllAsync().Returns(new List<AvalonWorld> { playerWorld, adminWorld });

        var ctx = new AuthPacketContext<CWorldListPacket>
        {
            Packet = new CWorldListPacket(),
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        _connection.DidNotReceive().Close();
        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task SendAllWorlds_WhenAccountIsAdministrator()
    {
        var account = MakeAccount(level: AccountAccessLevel.Administrator);
        _connection.AccountId.Returns(account.Id);
        _accountRepository.FindByIdAsync(account.Id).Returns(account);

        var playerWorld = MakeWorld(1, AccountAccessLevel.Player);
        var adminWorld = MakeWorld(2, AccountAccessLevel.Administrator);
        _worldRepository.FindAllAsync().Returns(new List<AvalonWorld> { playerWorld, adminWorld });

        var ctx = new AuthPacketContext<CWorldListPacket>
        {
            Packet = new CWorldListPacket(),
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task SendEmptyWorldList_WhenNoWorldsExist()
    {
        var account = MakeAccount();
        _connection.AccountId.Returns(account.Id);
        _accountRepository.FindByIdAsync(account.Id).Returns(account);
        _worldRepository.FindAllAsync().Returns(new List<AvalonWorld>());

        var ctx = new AuthPacketContext<CWorldListPacket>
        {
            Packet = new CWorldListPacket(),
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
        _connection.DidNotReceive().Close();
    }
}
