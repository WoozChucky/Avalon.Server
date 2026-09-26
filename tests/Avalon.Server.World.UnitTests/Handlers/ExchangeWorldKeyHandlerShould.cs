using Avalon.Common.Accounts;
using Avalon.Common.Cryptography;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Server.World.Handlers;
using Avalon.World;
using Avalon.World.Public;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Handlers;

public class ExchangeWorldKeyHandlerShould
{
    private readonly IAccountRepository _accountRepository = Substitute.For<IAccountRepository>();
    private readonly IWorldRepository _worldRepository = Substitute.For<IWorldRepository>();
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();
    private readonly IWorld _world = Substitute.For<IWorld>();
    private readonly IWorldConnection _connection = Substitute.For<IWorldConnection>();
    private readonly ICryptoManager _serverCrypto = Substitute.For<ICryptoManager>();
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
        _handler = MakeHandler(_cache);
    }

    private ExchangeWorldKeyHandler MakeHandler(IReplicatedCache cache) =>
        new ExchangeWorldKeyHandler(
            NullLogger<ExchangeWorldKeyHandler>.Instance,
            cache,
            _accountRepository,
            _world,
            _worldRepository);

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

        var expectedCacheKey = $"world:{_world.Id}:keys:{Convert.ToBase64String(worldKey)}";
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
        var expectedKey = $"world:{_world.Id}:keys:{Convert.ToBase64String(worldKey)}";

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
}
