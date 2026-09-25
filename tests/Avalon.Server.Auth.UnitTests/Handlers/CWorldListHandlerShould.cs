using Avalon.Common.Accounts;
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
using ProtoBuf;
using AvalonWorld = Avalon.Domain.Auth.World;

namespace Avalon.Server.Auth.UnitTests.Handlers;

public class CWorldListHandlerShould
{
    private readonly IWorldRepository _worldRepository = Substitute.For<IWorldRepository>();
    private readonly IAccountRepository _accountRepository = Substitute.For<IAccountRepository>();
    private readonly IAuthConnection _connection = Substitute.For<IAuthConnection>();
    private readonly IAvalonCryptoSession _cryptoSession = new FakeAvalonCryptoSession();
    private readonly CWorldListHandler _handler;

    public CWorldListHandlerShould()
    {
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
        var adminWorld = MakeWorld(2, AccountAccessLevel.Admin);
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
        var account = MakeAccount(level: AccountAccessLevel.Admin);
        _connection.AccountId.Returns(account.Id);
        _accountRepository.FindByIdAsync(account.Id).Returns(account);

        var playerWorld = MakeWorld(1, AccountAccessLevel.Player);
        var adminWorld = MakeWorld(2, AccountAccessLevel.Admin);
        _worldRepository.FindAllAsync().Returns(new List<AvalonWorld> { playerWorld, adminWorld });

        var ctx = new AuthPacketContext<CWorldListPacket>
        {
            Packet = new CWorldListPacket(),
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    /// <summary>
    /// #447: the list used an ordinal "required &lt;= actual" filter on a [Flags] enum, so any
    /// account holding PTR (32) or Tournament (16) was shown the Admin-only world, and staff were
    /// not shown the PTR world. World ids: 1 Admin, 2 Player, 3 PTR, 4 Tournament.
    /// </summary>
    [Theory]
    [InlineData(AccountAccessLevel.Player, new ushort[] { 2 })]
    [InlineData(AccountAccessLevel.PTR, new ushort[] { 2, 3 })]
    [InlineData(AccountAccessLevel.Player | AccountAccessLevel.PTR, new ushort[] { 2, 3 })]
    [InlineData(AccountAccessLevel.Tournament, new ushort[] { 2, 4 })]
    [InlineData(AccountAccessLevel.Player | AccountAccessLevel.GameMaster | AccountAccessLevel.Admin, new ushort[] { 1, 2, 3, 4 })]
    public async Task List_Exactly_The_Worlds_The_Account_May_Enter(AccountAccessLevel level, ushort[] expected)
    {
        var account = MakeAccount(level: level);
        _connection.AccountId.Returns(account.Id);
        _accountRepository.FindByIdAsync(account.Id).Returns(account);
        _worldRepository.FindAllAsync().Returns(new List<AvalonWorld>
        {
            MakeWorld(1, AccountAccessLevel.Admin),
            MakeWorld(2, AccountAccessLevel.Player),
            MakeWorld(3, AccountAccessLevel.PTR),
            MakeWorld(4, AccountAccessLevel.Tournament),
        });

        NetworkPacket? sent = null;
        _connection.When(c => c.Send(Arg.Any<NetworkPacket>())).Do(ci => sent = ci.Arg<NetworkPacket>());

        await _handler.ExecuteAsync(new AuthPacketContext<CWorldListPacket>
        {
            Packet = new CWorldListPacket(),
            Connection = _connection
        });

        // FakeAvalonCryptoSession.Encrypt is a pass-through, so the payload is the plain protobuf.
        Assert.NotNull(sent);
        using var stream = new MemoryStream(sent!.Payload);
        SWorldListPacket list = Serializer.Deserialize<SWorldListPacket>(stream);
        Assert.Equal(expected, (list.Worlds ?? []).Select(w => w.Id).Order().ToArray());
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
