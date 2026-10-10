using Avalon.Common.Accounts;
using Avalon.Common.Cryptography;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Network.Packets.Auth;
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
    private readonly IWorldReadiness _readiness = Substitute.For<IWorldReadiness>();
    private readonly IAuthConnection _connection = Substitute.For<IAuthConnection>();
    private readonly IAvalonCryptoSession _cryptoSession = new FakeAvalonCryptoSession();
    private readonly CWorldListHandler _handler;

    public CWorldListHandlerShould()
    {
        _connection.CryptoSession.Returns(_cryptoSession);
        _connection.Id.Returns(Guid.NewGuid());
        _handler = new CWorldListHandler(NullLoggerFactory.Instance, _worldRepository, _accountRepository,
            _readiness);
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
    // Staff below Admin: sees the PTR and Tournament worlds, not the Admin one.
    [InlineData(AccountAccessLevel.GameMaster, new ushort[] { 2, 3, 4 })]
    [InlineData(AccountAccessLevel.Player | AccountAccessLevel.GameMaster | AccountAccessLevel.Admin, new ushort[] { 1, 2, 3, 4 })]
    public async Task List_Exactly_The_Worlds_The_Account_May_Enter(AccountAccessLevel level, ushort[] expected)
    {
        Account account = MakeAccount(level: level);
        _connection.AccountId.Returns(account.Id);
        _accountRepository.FindByIdAsync(account.Id).Returns(account);
        _worldRepository.FindAllAsync().Returns(new List<AvalonWorld>
        {
            MakeWorld(1, AccountAccessLevel.Admin),
            MakeWorld(2, AccountAccessLevel.Player),
            MakeWorld(3, AccountAccessLevel.PTR),
            MakeWorld(4, AccountAccessLevel.Tournament),
        });

        OutboundPacket sent = default;
        _connection.When(c => c.Send(Arg.Any<OutboundPacket>())).Do(ci => sent = ci.Arg<OutboundPacket>());

        await _handler.ExecuteAsync(new AuthPacketContext<CWorldListPacket>
        {
            Packet = new CWorldListPacket(),
            Connection = _connection
        });

        // A packet is sealed only as the outbox frames it, so the payload is the plain protobuf.
        Assert.NotNull(sent.Payload);
        using var stream = new MemoryStream(sent.PayloadMemory.ToArray());
        SWorldListPacket list = Serializer.Deserialize<SWorldListPacket>(stream);
        Assert.Equal(expected, (list.Worlds ?? []).Select(w => w.Id).Order().ToArray());
    }

    [Fact]
    public async Task SendEmptyWorldList_WhenNoWorldsExist()
    {
        Account account = MakeAccount();
        _connection.AccountId.Returns(account.Id);
        _accountRepository.FindByIdAsync(account.Id).Returns(account);
        _worldRepository.FindAllAsync().Returns(new List<AvalonWorld>());

        var ctx = new AuthPacketContext<CWorldListPacket>
        {
            Packet = new CWorldListPacket(),
            Connection = _connection
        };

        await _handler.ExecuteAsync(ctx);

        _connection.Received(1).Send(Arg.Any<OutboundPacket>());
        _connection.DidNotReceive().Close();
    }

    [Fact]
    public async Task Send_derived_runtime_status_with_maintenance_precedence()
    {
        Account account = MakeAccount(level: AccountAccessLevel.Admin);
        _connection.AccountId.Returns(account.Id);
        _accountRepository.FindByIdAsync(account.Id).Returns(account);
        AvalonWorld maintenance = MakeWorld(1);
        maintenance.MaintenanceEnabled = true;
        _worldRepository.FindAllAsync().Returns(new List<AvalonWorld>
        {
            maintenance, MakeWorld(2), MakeWorld(3),
        });
        _readiness.IsReadyAsync(1, Arg.Any<CancellationToken>()).Returns(true);
        _readiness.IsReadyAsync(2, Arg.Any<CancellationToken>()).Returns(true);
        _readiness.IsReadyAsync(3, Arg.Any<CancellationToken>()).Returns(false);
        OutboundPacket sent = default;
        _connection.When(c => c.Send(Arg.Any<OutboundPacket>())).Do(call => sent = call.Arg<OutboundPacket>());

        await _handler.ExecuteAsync(new AuthPacketContext<CWorldListPacket>
        { Packet = new CWorldListPacket(), Connection = _connection });

        using var stream = new MemoryStream(sent.PayloadMemory.ToArray());
        Dictionary<ushort, short> statuses = Serializer.Deserialize<SWorldListPacket>(stream).Worlds!.ToDictionary(w => w.Id, w => w.Status);
        Assert.Equal((short)WorldStatus.Maintenance, statuses[1]);
        Assert.Equal((short)WorldStatus.Online, statuses[2]);
        Assert.Equal((short)WorldStatus.Offline, statuses[3]);
    }

    [Fact]
    public async Task Show_a_ready_scheduled_world_as_online()
    {
        Account account = MakeAccount();
        _connection.AccountId.Returns(account.Id);
        _accountRepository.FindByIdAsync(account.Id).Returns(account);
        AvalonWorld scheduled = MakeWorld(1);
        scheduled.MaintenanceEnabled = true;
        scheduled.MaintenanceDeadlineUtc = DateTime.UtcNow.AddMinutes(10);
        _worldRepository.FindAllAsync().Returns(new List<AvalonWorld> { scheduled });
        _readiness.IsReadyAsync(1, Arg.Any<CancellationToken>()).Returns(true);
        OutboundPacket sent = default;
        _connection.When(c => c.Send(Arg.Any<OutboundPacket>())).Do(call => sent = call.Arg<OutboundPacket>());

        await _handler.ExecuteAsync(new AuthPacketContext<CWorldListPacket>
        { Packet = new CWorldListPacket(), Connection = _connection });

        using var stream = new MemoryStream(sent.PayloadMemory.ToArray());
        Assert.Equal((short)WorldStatus.Online,
            Assert.Single(Serializer.Deserialize<SWorldListPacket>(stream).Worlds!).Status);
    }
}
