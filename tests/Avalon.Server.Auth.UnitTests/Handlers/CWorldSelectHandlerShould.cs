using Avalon.Common.Cryptography;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Server.Auth;
using Avalon.Server.Auth.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using AvalonWorld = Avalon.Domain.Auth.World;

namespace Avalon.Server.Auth.UnitTests.Handlers;

public class CWorldSelectHandlerShould
{
    private readonly IAccountRepository _accountRepository = Substitute.For<IAccountRepository>();
    private readonly IWorldRepository _worldRepository = Substitute.For<IWorldRepository>();
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();
    private readonly IAuthConnection _connection = Substitute.For<IAuthConnection>();
    private readonly IAvalonCryptoSession _cryptoSession = Substitute.For<IAvalonCryptoSession>();
    private readonly CWorldSelectHandler _handler;

    public CWorldSelectHandlerShould()
    {
        _cryptoSession.Encrypt(Arg.Any<byte[]>()).Returns(x => (byte[])x[0]);
        _connection.CryptoSession.Returns(_cryptoSession);
        _connection.Id.Returns(Guid.NewGuid());
        _handler = new CWorldSelectHandler(NullLoggerFactory.Instance, _cache, _accountRepository, _worldRepository);
    }

    private static Account MakeAccount(AccountAccessLevel level = AccountAccessLevel.Player)
        => new Account
        {
            Username = "TESTUSER",
            Salt = new byte[16],
            Verifier = new byte[20],
            Email = "test@test.com",
            JoinDate = DateTime.UtcNow,
            AccessLevel = level,
            Id = new AccountId(5L)
        };

    private static AvalonWorld MakeWorld(ushort id = 1, AccountAccessLevel req = AccountAccessLevel.Player)
        => new AvalonWorld
        {
            Name = "Test World",
            Host = "localhost",
            Port = 7001,
            MinVersion = "0.0.1",
            Version = "0.0.1",
            AccessLevelRequired = req,
            Id = new WorldId(id)
        };

    [Fact]
    public async Task CloseConnection_WhenAccountNotFound()
    {
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>()).Returns((Account?)null);
        _connection.AccountId.Returns((AccountId?)null);

        var ctx = new AuthPacketContext<CWorldSelectPacket>
        {
            Packet = new CWorldSelectPacket { WorldId = new WorldId(1) },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        _connection.Received(1).Close();
    }

    [Fact]
    public async Task DoNothing_WhenWorldNotFound()
    {
        var account = MakeAccount();
        _connection.AccountId.Returns(account.Id);
        _accountRepository.FindByIdAsync(account.Id).Returns(account);
        _worldRepository.FindByIdAsync(Arg.Any<WorldId>()).Returns((AvalonWorld?)null);

        var ctx = new AuthPacketContext<CWorldSelectPacket>
        {
            Packet = new CWorldSelectPacket { WorldId = new WorldId(99) },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        _connection.DidNotReceive().Close();
        await _cache.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>());
        await _cache.DidNotReceive().PublishAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task DoNothing_WhenAccountLacksRequiredAccessLevel()
    {
        var account = MakeAccount(level: AccountAccessLevel.Player);
        _connection.AccountId.Returns(account.Id);
        _accountRepository.FindByIdAsync(account.Id).Returns(account);
        var world = MakeWorld(req: AccountAccessLevel.Administrator);
        _worldRepository.FindByIdAsync(Arg.Any<WorldId>()).Returns(world);

        var ctx = new AuthPacketContext<CWorldSelectPacket>
        {
            Packet = new CWorldSelectPacket { WorldId = new WorldId(1) },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        await _cache.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>());
        await _cache.DidNotReceive().PublishAsync(Arg.Any<string>(), Arg.Any<string>());
        await _accountRepository.DidNotReceive().UpdateAsync(Arg.Any<Account>());
    }

    [Fact]
    public async Task SaveSessionKey_AndPublishToCache_WhenSelectionSucceeds()
    {
        var account = MakeAccount(level: AccountAccessLevel.Player);
        _connection.AccountId.Returns(account.Id);
        _accountRepository.FindByIdAsync(account.Id).Returns(account);
        var world = MakeWorld(1, AccountAccessLevel.Player);
        _worldRepository.FindByIdAsync(Arg.Any<WorldId>()).Returns(world);

        var ctx = new AuthPacketContext<CWorldSelectPacket>
        {
            Packet = new CWorldSelectPacket { WorldId = new WorldId(1) },
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        Assert.NotNull(account.SessionKey);
        Assert.Equal(32, account.SessionKey.Length);
        await _accountRepository.Received(1).UpdateAsync(account);
        await _cache.Received(1).SetAsync(
            Arg.Is<string>(k => k.StartsWith($"world:{world.Id}:keys:")),
            Arg.Any<string>(),
            Arg.Is<TimeSpan?>(t => t == TimeSpan.FromMinutes(5)));
        await _cache.Received(1).PublishAsync(
            Arg.Is<string>(k => k == $"world:{world.Id}:select"),
            Arg.Is<string>(v => v.StartsWith($"account:{account.Id}:worldKey:")));
    }
}
