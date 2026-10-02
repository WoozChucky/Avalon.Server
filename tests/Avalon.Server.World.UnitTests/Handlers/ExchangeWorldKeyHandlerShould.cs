using Avalon.Common.Accounts;
using Avalon.Common.Cryptography;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Generic;
using Avalon.Server.World.Handlers;
using Avalon.World;
using Avalon.World.Public;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using ProtoBuf;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World.Maintenance;
using Avalon.World.Persistence;

namespace Avalon.Server.World.UnitTests.Handlers;

public class ExchangeWorldKeyHandlerShould
{
    private readonly IAccountRepository _accountRepository = Substitute.For<IAccountRepository>();
    private readonly IWorldRepository _worldRepository = Substitute.For<IWorldRepository>();
    private readonly IWorldMaintenanceRepository _maintenance = Substitute.For<IWorldMaintenanceRepository>();
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();
    private readonly IWorld _world = Substitute.For<IWorld>();
    private readonly IWorldConnection _connection = Substitute.For<IWorldConnection>();
    private readonly ICryptoManager _serverCrypto = Substitute.For<ICryptoManager>();
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));
    private readonly ExchangeWorldKeyHandler _handler;

    private const int ValidKeySize = 32;

    public ExchangeWorldKeyHandlerShould()
    {
        _serverCrypto.GetValidKeySize().Returns(ValidKeySize);
        _serverCrypto.GetPublicKey().Returns(new byte[ValidKeySize]);
        _connection.ServerCrypto.Returns(_serverCrypto);
        _connection.RemoteEndPoint.Returns("127.0.0.1:12345");
        _world.Id.Returns(new WorldId(1));
        // Redis DEL reports whether it deleted anything; by default the key is there to delete.
        _cache.RemoveAsync(Arg.Any<string>()).Returns(true);
        _worldRepository.FindByIdAsync(Arg.Any<WorldId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(MakeWorld(AccountAccessLevel.Player));
        _maintenance.ReadAsync(Arg.Any<WorldId>(), Arg.Any<CancellationToken>())
            .Returns(new WorldMaintenanceState(false, 0, null));
        _handler = MakeHandler(_cache);
    }

    private ExchangeWorldKeyHandler MakeHandler(IReplicatedCache cache) =>
        new ExchangeWorldKeyHandler(
            NullLogger<ExchangeWorldKeyHandler>.Instance,
            cache,
            _accountRepository,
            _world,
            _worldRepository,
            _maintenance, _clock);

    [Fact]
    public async Task Refuse_a_previously_issued_key_at_the_maintenance_deadline()
    {
        _cache.GetAsync(Arg.Any<string>()).Returns("42:0");
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>()).Returns(MakeAccount(42));
        _maintenance.ReadAsync(new WorldId(1), Arg.Any<CancellationToken>())
            .Returns(new WorldMaintenanceState(true, 1, _clock.Now.UtcDateTime));

        await _handler.ExecuteAsync(MakeCtx(new byte[32], new byte[ValidKeySize]));

        var calls = _connection.ReceivedCalls().Select(c => c.GetMethodInfo().Name).ToList();
        Assert.True(calls.IndexOf(nameof(IWorldConnection.Send)) < calls.IndexOf(nameof(IWorldConnection.Close)));
        var packet = _connection.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IWorldConnection.Send))
            .Select(c => c.GetArguments()[0]).OfType<NetworkPacket>().Single();
        Assert.Equal(DisconnectReason.Maintenance,
            Serializer.Deserialize<SDisconnectPacket>(new MemoryStream(packet.Payload)).ReasonCode);
        _connection.DidNotReceive().AccountId = Arg.Any<AccountId>();
        _connection.CryptoSession.DidNotReceive().Initialize(Arg.Any<byte[]>());
        await _cache.DidNotReceive().RemoveAsync("account:42:inWorld");
    }

    [Fact]
    public async Task Admit_a_Player_during_the_countdown()
    {
        _cache.GetAsync(Arg.Any<string>()).Returns("42:0");
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>()).Returns(MakeAccount(42));
        _maintenance.ReadAsync(new WorldId(1), Arg.Any<CancellationToken>())
            .Returns(new WorldMaintenanceState(true, 1, _clock.Now.UtcDateTime.AddMinutes(10)));

        await _handler.ExecuteAsync(MakeCtx(new byte[32], new byte[ValidKeySize]));

        _connection.Received().AccountId = (AccountId)42L;
    }

    [Fact]
    public async Task Refuse_when_the_deadline_arrives_while_exchange_waits_for_maintenance_read()
    {
        _cache.GetAsync(Arg.Any<string>()).Returns("42:0");
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>()).Returns(MakeAccount(42));
        var read = new TaskCompletionSource<WorldMaintenanceState?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _maintenance.ReadAsync(new WorldId(1), Arg.Any<CancellationToken>()).Returns(read.Task);
        DateTime deadline = _clock.Now.UtcDateTime.AddMinutes(10);

        Task exchange = _handler.ExecuteAsync(MakeCtx(new byte[32], new byte[ValidKeySize]));
        Assert.True(SpinWait.SpinUntil(() => _maintenance.ReceivedCalls().Any(c =>
            c.GetMethodInfo().Name == nameof(IWorldMaintenanceRepository.ReadAsync)), TimeSpan.FromSeconds(5)));
        _clock.Now = new DateTimeOffset(deadline);
        read.SetResult(new WorldMaintenanceState(true, 1, deadline));
        await exchange;

        _connection.DidNotReceive().AccountId = Arg.Any<AccountId>();
        _connection.Received(1).Close();
    }

    [Fact]
    public async Task Admit_an_Admin_with_a_valid_key_during_maintenance()
    {
        _cache.GetAsync(Arg.Any<string>()).Returns("42:0");
        var account = MakeAccount(42);
        account.AccessLevel = AccountAccessLevel.Admin;
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>()).Returns(account);
        _maintenance.ReadAsync(new WorldId(1), Arg.Any<CancellationToken>())
            .Returns(new WorldMaintenanceState(true, 1, _clock.Now.UtcDateTime));

        await _handler.ExecuteAsync(MakeCtx(new byte[32], new byte[ValidKeySize]));

        _connection.Received().AccountId = (AccountId)42L;
        _connection.DidNotReceive().Close();
        await _cache.Received(1).RemoveAsync("account:42:inWorld");
    }

    [Fact]
    public async Task Publish_Admin_access_before_identity_becomes_visible_to_the_maintenance_tick()
    {
        _cache.GetAsync(Arg.Any<string>()).Returns("42:0");
        var account = MakeAccount(42);
        account.AccessLevel = AccountAccessLevel.Admin;
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>()).Returns(account);
        _maintenance.ReadAsync(new WorldId(1), Arg.Any<CancellationToken>())
            .Returns(new WorldMaintenanceState(true, 1, _clock.Now.UtcDateTime));

        var coordinator = new WorldMaintenanceCoordinator(new WorldId(1), _maintenance,
            Substitute.For<ICharacterSaver>(), TimeProvider.System,
            NullLogger<WorldMaintenanceCoordinator>.Instance);
        coordinator.ApplyCommitted(new WorldMaintenanceState(true, 1, _clock.Now.UtcDateTime));
        var connection = Substitute.For<IWorldConnection, IAccessLevelAssignable, IMaintenanceBlockable>();
        connection.ServerCrypto.Returns(_serverCrypto);
        connection.IsConnected.Returns(true);
        connection.CloseAsync().Returns(Task.CompletedTask);
        AccountId? publishedId = null;
        AccountAccessLevel publishedAccess = AccountAccessLevel.Player;
        connection.AccountId.Returns(_ => publishedId);
        connection.AccessLevel.Returns(_ => publishedAccess);
        ((IAccessLevelAssignable)connection).When(c => c.AssignAccessLevel(Arg.Any<AccountAccessLevel>()))
            .Do(call => publishedAccess = (AccountAccessLevel)call[0]!);
        connection.When(c => c.AccountId = Arg.Any<AccountId>()).Do(call =>
        {
            publishedId = (AccountId)call[0]!;
            coordinator.Advance(_clock.Now.UtcDateTime, [connection]);
        });

        await _handler.ExecuteAsync(MakeCtx(new byte[32], new byte[ValidKeySize], connection));

        Assert.Equal(AccountAccessLevel.Admin, publishedAccess);
        ((IMaintenanceBlockable)connection).DidNotReceive().BlockForMaintenance();
        _ = connection.DidNotReceive().CloseAsync();
    }

    [Fact]
    public async Task Assign_Admin_access_at_exchange_before_character_selection()
    {
        _cache.GetAsync(Arg.Any<string>()).Returns("42:0");
        var account = MakeAccount(42);
        account.AccessLevel = AccountAccessLevel.Admin;
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>()).Returns(account);
        var connection = Substitute.For<IWorldConnection, IAccessLevelAssignable>();
        connection.ServerCrypto.Returns(_serverCrypto);

        await _handler.ExecuteAsync(MakeCtx(new byte[32], new byte[ValidKeySize], connection));

        ((IAccessLevelAssignable)connection).Received(1).AssignAccessLevel(AccountAccessLevel.Admin);
    }

    [Fact]
    public async Task Ignore_a_second_exchange_on_an_authenticated_connection_before_reading_or_spending_anything()
    {
        _cache.GetAsync(Arg.Any<string>()).Returns("42:0");
        var admin = MakeAccount(42);
        admin.AccessLevel = AccountAccessLevel.Admin;
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>()).Returns(admin);
        var connection = Substitute.For<IWorldConnection, IAccessLevelAssignable>();
        connection.AccountId.Returns(new AccountId(7));
        connection.ServerCrypto.Returns(_serverCrypto);

        await _handler.ExecuteAsync(MakeCtx(new byte[32], new byte[ValidKeySize], connection));

        await _cache.DidNotReceiveWithAnyArgs().GetAsync(default!);
        await _cache.DidNotReceiveWithAnyArgs().RemoveAsync(default!);
        await _accountRepository.DidNotReceiveWithAnyArgs().FindByIdAsync(default!, default, default);
        connection.DidNotReceive().AccountId = Arg.Any<AccountId>();
        ((IAccessLevelAssignable)connection).DidNotReceiveWithAnyArgs().AssignAccessLevel(default);
        connection.DidNotReceiveWithAnyArgs().Send(default!);
        connection.DidNotReceive().Close();
    }

    [Fact]
    public async Task Close_a_slow_exchange_as_unavailable_when_maintenance_is_off()
    {
        _cache.GetAsync(Arg.Any<string>()).Returns("42:0");
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>()).Returns(MakeAccount(42));
        // The step between the maintenance read and the acceptance takes longer than the decision lasts.
        _cache.RemoveAsync("account:42:inWorld").Returns(_ =>
        {
            _clock.Now += TimeSpan.FromSeconds(6);
            return true;
        });

        await _handler.ExecuteAsync(MakeCtx(new byte[32], new byte[ValidKeySize]));

        Assert.Equal(DisconnectReason.Unknown, SentDisconnectReason());
        _connection.Received(1).Close();
        _connection.DidNotReceive().AccountId = Arg.Any<AccountId>();
    }

    [Fact]
    public async Task Close_a_slow_exchange_as_maintenance_when_the_deadline_passed_meanwhile()
    {
        _cache.GetAsync(Arg.Any<string>()).Returns("42:0");
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>()).Returns(MakeAccount(42));
        _maintenance.ReadAsync(new WorldId(1), Arg.Any<CancellationToken>())
            .Returns(new WorldMaintenanceState(true, 1, _clock.Now.UtcDateTime.AddSeconds(3)));
        _cache.RemoveAsync("account:42:inWorld").Returns(_ =>
        {
            _clock.Now += TimeSpan.FromSeconds(6);
            return true;
        });

        await _handler.ExecuteAsync(MakeCtx(new byte[32], new byte[ValidKeySize]));

        Assert.Equal(DisconnectReason.Maintenance, SentDisconnectReason());
        _connection.DidNotReceive().AccountId = Arg.Any<AccountId>();
    }

    private DisconnectReason SentDisconnectReason()
    {
        NetworkPacket packet = _connection.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IWorldConnection.Send))
            .Select(c => c.GetArguments()[0]).OfType<NetworkPacket>()
            .Single(p => p.Header.Type == NetworkPacketType.SMSG_DISCONNECT);
        return Serializer.Deserialize<SDisconnectPacket>(new MemoryStream(packet.Payload)).ReasonCode;
    }

    [Fact]
    public async Task Refuse_entry_when_maintenance_database_is_unreadable()
    {
        _cache.GetAsync(Arg.Any<string>()).Returns("42:0");
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>()).Returns(MakeAccount(42));
        _maintenance.ReadAsync(new WorldId(1), Arg.Any<CancellationToken>())
            .Returns<Task<WorldMaintenanceState?>>(_ => throw new InvalidOperationException("database offline"));

        await _handler.ExecuteAsync(MakeCtx(new byte[32], new byte[ValidKeySize]));

        _connection.Received(1).Close();
        _connection.DidNotReceive().AccountId = Arg.Any<AccountId>();
        await _cache.DidNotReceive().RemoveAsync("account:42:inWorld");
    }

    private static Avalon.Domain.Auth.World MakeWorld(AccountAccessLevel required)
        => new Avalon.Domain.Auth.World
        {
            Id = new WorldId(1),
            Name = "Test",
            Host = "127.0.0.1",
            Port = 21001,
            MinVersion = "0.0.1",
            Version = "0.0.1",
            AccessLevelRequired = required
        };

    private IWorldConnection MakeConnection()
    {
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.ServerCrypto.Returns(_serverCrypto);
        connection.RemoteEndPoint.Returns("127.0.0.1:12345");
        return connection;
    }

    private static Account MakeAccount(long id = 1)
        => new Account
        {
            Username = "TESTUSER",
            Salt = new byte[16],
            Verifier = new byte[20],
            Email = "test@test.com",
            JoinDate = DateTime.UtcNow,
            Id = new AccountId(id)
        };

    private WorldPacketContext<CExchangeWorldKeyPacket> MakeCtx(byte[] worldKey, byte[] publicKey,
        IWorldConnection? connection = null) =>
        new WorldPacketContext<CExchangeWorldKeyPacket>
        {
            Packet = new CExchangeWorldKeyPacket { WorldKey = worldKey, PublicKey = publicKey },
            Connection = connection ?? _connection
        };

    [Fact]
    public async Task DoNothing_WhenWorldKeyNotFoundInCache()
    {
        _cache.GetAsync(Arg.Any<string>()).Returns((string?)null);

        await _handler.ExecuteAsync(MakeCtx(new byte[32], new byte[ValidKeySize]));

        _connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
        await _cache.DidNotReceive().RemoveAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task SpendKeyButKeepInWorldFlag_WhenExchangeIsRefusedAfterTheKeyIsFound()
    {
        _cache.GetAsync(Arg.Any<string>()).Returns("1:0");
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>()).Returns((Account?)null);

        await _handler.ExecuteAsync(MakeCtx(new byte[32], new byte[ValidKeySize]));

        // The key is spent as soon as it is found, so a refused exchange cannot be retried with it,
        // and a refused exchange never releases the duplicate-session mutex.
        await _cache.Received(1).RemoveAsync(Arg.Is<string>(k => k.StartsWith("world:")));
        await _cache.DidNotReceive().RemoveAsync(Arg.Is<string>(k => k.StartsWith("account:")));
        _connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task DoNothing_WhenCachedIdIsNotAValidNumber()
    {
        _cache.GetAsync(Arg.Any<string>()).Returns("not-a-number");

        await _handler.ExecuteAsync(MakeCtx(new byte[32], new byte[ValidKeySize]));

        _connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
        await _accountRepository.DidNotReceive().FindByIdAsync(Arg.Any<AccountId>());
    }

    [Fact]
    public async Task DoNothing_WhenAccountNotFound()
    {
        _cache.GetAsync(Arg.Any<string>()).Returns("42:0");
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>()).Returns((Account?)null);

        await _handler.ExecuteAsync(MakeCtx(new byte[32], new byte[ValidKeySize]));

        _connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task DoNothing_WhenPublicKeyIsEmpty()
    {
        _cache.GetAsync(Arg.Any<string>()).Returns("42:0");
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>()).Returns(MakeAccount(42));

        await _handler.ExecuteAsync(MakeCtx(new byte[32], Array.Empty<byte>()));

        _connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
        _connection.CryptoSession.DidNotReceive().Initialize(Arg.Any<byte[]>());
    }

    [Fact]
    public async Task DoNothing_WhenPublicKeySizeIsInvalid()
    {
        _cache.GetAsync(Arg.Any<string>()).Returns("42:0");
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>()).Returns(MakeAccount(42));

        await _handler.ExecuteAsync(MakeCtx(new byte[32], new byte[ValidKeySize + 8]));

        _connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
        _connection.CryptoSession.DidNotReceive().Initialize(Arg.Any<byte[]>());
    }

    /// <summary>
    /// #495: the key was issued at version 0; a password change has moved the account to 1 in the
    /// five minutes the key lives. It is spent, and refused, and the in-world slot is kept.
    /// </summary>
    [Fact]
    public async Task Refuse_a_key_issued_before_the_credentials_changed()
    {
        _cache.GetAsync(Arg.Any<string>()).Returns("42:0");
        Account account = MakeAccount(42);
        account.CredentialsVersion = 1;
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>()).Returns(account);

        await _handler.ExecuteAsync(MakeCtx(new byte[32], new byte[ValidKeySize]));

        _connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
        _connection.CryptoSession.DidNotReceive().Initialize(Arg.Any<byte[]>());
        await _cache.Received(1).RemoveAsync(Arg.Is<string>(k => k.StartsWith("world:")));
        await _cache.DidNotReceive().RemoveAsync(Arg.Is<string>(k => k.EndsWith(":inWorld")));
    }

    [Theory]
    [InlineData("42")]
    [InlineData("42:")]
    [InlineData("42:x")]
    public async Task Refuse_a_key_whose_value_carries_no_credentials_version(string value)
    {
        _cache.GetAsync(Arg.Any<string>()).Returns(value);
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>()).Returns(MakeAccount(42));

        await _handler.ExecuteAsync(MakeCtx(new byte[32], new byte[ValidKeySize]));

        _connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task SetAccountIdAndSendKey_WhenAllValidationsPass()
    {
        var worldKey = new byte[32];
        var publicKey = new byte[ValidKeySize];
        new Random().NextBytes(worldKey);
        new Random().NextBytes(publicKey);

        // Named by the key's SHA-256, never the key itself (#535).
        var expectedCacheKey = $"world:{_world.Id}:keys:{Sha256Hex(Convert.ToBase64String(worldKey))}";
        _cache.GetAsync(expectedCacheKey).Returns("42:0");
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>()).Returns(MakeAccount(42));

        await _handler.ExecuteAsync(MakeCtx(worldKey, publicKey));

        _connection.CryptoSession.Received(1).Initialize(publicKey);
        _connection.Received().AccountId = (AccountId)42L;
        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task LookupCacheWithCorrectKey_IncludingBase64WorldKey()
    {
        var worldKey = new byte[32];
        new Random().NextBytes(worldKey);
        var expectedKey = $"world:{_world.Id}:keys:{Sha256Hex(Convert.ToBase64String(worldKey))}";

        _cache.GetAsync(expectedKey).Returns((string?)null);

        await _handler.ExecuteAsync(MakeCtx(worldKey, new byte[ValidKeySize]));

        await _cache.Received(1).GetAsync(expectedKey);
    }

    [Fact]
    public async Task ClearInWorldFlag_WhenKeyExchangeSucceeds()
    {
        var worldKey = new byte[32];
        var publicKey = new byte[ValidKeySize];
        new Random().NextBytes(worldKey);
        new Random().NextBytes(publicKey);

        _cache.GetAsync(Arg.Any<string>()).Returns("42:0");
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>()).Returns(MakeAccount(42));

        await _handler.ExecuteAsync(MakeCtx(worldKey, publicKey));

        await _cache.Received(1).RemoveAsync($"account:42:inWorld");
    }

    // #450: two connections presenting the same key at the same time. Both reads see the key
    // before either delete runs; only one may be accepted.
    [Fact]
    public async Task AcceptExactlyOneConnection_WhenTheSameKeyIsSpentConcurrently()
    {
        IReplicatedCache cache = Substitute.For<IReplicatedCache>();
        var readsHeld = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bothRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int reads = 0;
        cache.GetAsync(Arg.Is<string>(k => k.StartsWith("world:"))).Returns(_ =>
        {
            if (Interlocked.Increment(ref reads) == 2) bothRead.TrySetResult();
            return readsHeld.Task;
        });

        // Models Redis DEL: only the first delete of the key reports that it deleted something.
        int keyDeletes = 0;
        cache.RemoveAsync(Arg.Is<string>(k => k.StartsWith("world:")))
            .Returns(_ => Task.FromResult(Interlocked.Increment(ref keyDeletes) == 1));
        cache.RemoveAsync(Arg.Is<string>(k => k.StartsWith("account:"))).Returns(true);

        _accountRepository.FindByIdAsync(Arg.Any<AccountId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(MakeAccount(42));

        ExchangeWorldKeyHandler handler = MakeHandler(cache);
        IWorldConnection first = MakeConnection();
        IWorldConnection second = MakeConnection();
        var worldKey = new byte[32];
        new Random().NextBytes(worldKey);

        Task firstExchange = handler.ExecuteAsync(MakeCtx(worldKey, new byte[ValidKeySize], first));
        Task secondExchange = handler.ExecuteAsync(MakeCtx(worldKey, new byte[ValidKeySize], second));

        await bothRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        readsHeld.SetResult("42:0");
        await Task.WhenAll(firstExchange, secondExchange).WaitAsync(TimeSpan.FromSeconds(5));

        int accepted = CountSends(first) + CountSends(second);
        Assert.Equal(1, accepted);
        await cache.Received(1).RemoveAsync("account:42:inWorld");
    }

    private static int CountSends(IWorldConnection connection)
        => connection.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IWorldConnection.Send));

    // #450: access is checked when the key is issued, but the key lives five minutes. An account
    // demoted in that window must not get in.
    [Theory]
    [InlineData(AccountAccessLevel.GameMaster, AccountAccessLevel.Player)]
    [InlineData(AccountAccessLevel.Admin, AccountAccessLevel.GameMaster)]
    [InlineData(AccountAccessLevel.PTR, AccountAccessLevel.Player)]
    [InlineData(AccountAccessLevel.Tournament, AccountAccessLevel.PTR)]
    public async Task Refuse_WhenAccountNoLongerSatisfiesTheWorldAccessLevel(
        AccountAccessLevel required, AccountAccessLevel actual)
    {
        _worldRepository.FindByIdAsync(Arg.Any<WorldId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(MakeWorld(required));
        Account account = MakeAccount(42);
        account.AccessLevel = actual;
        _cache.GetAsync(Arg.Any<string>()).Returns("42:0");
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(account);

        await _handler.ExecuteAsync(MakeCtx(new byte[32], new byte[ValidKeySize]));

        _connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
        _connection.CryptoSession.DidNotReceive().Initialize(Arg.Any<byte[]>());
        await _cache.DidNotReceive().RemoveAsync("account:42:inWorld");
    }

    [Theory]
    [InlineData(AccountAccessLevel.PTR, AccountAccessLevel.PTR)]
    [InlineData(AccountAccessLevel.PTR, AccountAccessLevel.GameMaster)]
    [InlineData(AccountAccessLevel.Player, AccountAccessLevel.Tournament)]
    [InlineData(AccountAccessLevel.Admin, AccountAccessLevel.Admin)]
    public async Task Accept_WhenAccountStillSatisfiesTheWorldAccessLevel(
        AccountAccessLevel required, AccountAccessLevel actual)
    {
        _worldRepository.FindByIdAsync(Arg.Any<WorldId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(MakeWorld(required));
        Account account = MakeAccount(42);
        account.AccessLevel = actual;
        _cache.GetAsync(Arg.Any<string>()).Returns("42:0");
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(account);

        await _handler.ExecuteAsync(MakeCtx(new byte[32], new byte[ValidKeySize]));

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    // #450: an account banned or deactivated after its key was issued must not get in.
    [Theory]
    [InlineData(AccountStatus.Banned)]
    [InlineData(AccountStatus.Deactivated)]
    public async Task Refuse_WhenAccountIsNotActive(AccountStatus status)
    {
        Account account = MakeAccount(42);
        account.Status = status;
        _cache.GetAsync(Arg.Any<string>()).Returns("42:0");
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(account);

        await _handler.ExecuteAsync(MakeCtx(new byte[32], new byte[ValidKeySize]));

        _connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
        _connection.CryptoSession.DidNotReceive().Initialize(Arg.Any<byte[]>());
        await _cache.DidNotReceive().RemoveAsync("account:42:inWorld");
    }

    [Fact]
    public async Task Refuse_WhenWorldRowCannotBeFound()
    {
        _worldRepository.FindByIdAsync(Arg.Any<WorldId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns((Avalon.Domain.Auth.World?)null);
        _cache.GetAsync(Arg.Any<string>()).Returns("42:0");
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(MakeAccount(42));

        await _handler.ExecuteAsync(MakeCtx(new byte[32], new byte[ValidKeySize]));

        _connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
        await _cache.DidNotReceive().RemoveAsync("account:42:inWorld");
    }

    [Fact]
    public async Task Refuse_WhenKeyWasAlreadySpent()
    {
        _cache.GetAsync(Arg.Any<string>()).Returns("42:0");
        _cache.RemoveAsync(Arg.Is<string>(k => k.StartsWith("world:"))).Returns(false);
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(MakeAccount(42));

        await _handler.ExecuteAsync(MakeCtx(new byte[32], new byte[ValidKeySize]));

        _connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
        _connection.CryptoSession.DidNotReceive().Initialize(Arg.Any<byte[]>());
        await _cache.DidNotReceive().RemoveAsync("account:42:inWorld");
    }

    [Fact]
    public async Task KeepInWorldFlag_WhenPublicKeyIsInvalid()
    {
        _cache.GetAsync(Arg.Any<string>()).Returns("42:0");
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(MakeAccount(42));

        await _handler.ExecuteAsync(MakeCtx(new byte[32], Array.Empty<byte>()));

        await _cache.DidNotReceive().RemoveAsync("account:42:inWorld");
    }

    private static string Sha256Hex(string value) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}
