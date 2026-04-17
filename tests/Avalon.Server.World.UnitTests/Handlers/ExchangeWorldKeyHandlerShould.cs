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
        _handler = new ExchangeWorldKeyHandler(
            NullLogger<ExchangeWorldKeyHandler>.Instance,
            _cache,
            _accountRepository,
            _world);
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

    private WorldPacketContext<CExchangeWorldKeyPacket> MakeCtx(byte[] worldKey, byte[] publicKey) =>
        new WorldPacketContext<CExchangeWorldKeyPacket>
        {
            Packet = new CExchangeWorldKeyPacket { WorldKey = worldKey, PublicKey = publicKey },
            Connection = _connection
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
    public async Task RemoveCacheKey_WhenWorldKeyFoundInCache()
    {
        _cache.GetAsync(Arg.Any<string>()).Returns("1");
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>()).Returns((Account?)null);

        await _handler.ExecuteAsync(MakeCtx(new byte[32], new byte[ValidKeySize]));

        // Both the world key and the inWorld flag are removed once the key is validated
        await _cache.Received(1).RemoveAsync(Arg.Is<string>(k => k.StartsWith("world:")));
        await _cache.Received(1).RemoveAsync(Arg.Is<string>(k => k.StartsWith("account:")));
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
        _cache.GetAsync(Arg.Any<string>()).Returns("42");
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>()).Returns((Account?)null);

        await _handler.ExecuteAsync(MakeCtx(new byte[32], new byte[ValidKeySize]));

        _connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public async Task DoNothing_WhenPublicKeyIsEmpty()
    {
        _cache.GetAsync(Arg.Any<string>()).Returns("42");
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>()).Returns(MakeAccount(42));

        await _handler.ExecuteAsync(MakeCtx(new byte[32], Array.Empty<byte>()));

        _connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
        _connection.CryptoSession.DidNotReceive().Initialize(Arg.Any<byte[]>());
    }

    [Fact]
    public async Task DoNothing_WhenPublicKeySizeIsInvalid()
    {
        _cache.GetAsync(Arg.Any<string>()).Returns("42");
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>()).Returns(MakeAccount(42));

        await _handler.ExecuteAsync(MakeCtx(new byte[32], new byte[ValidKeySize + 8]));

        _connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
        _connection.CryptoSession.DidNotReceive().Initialize(Arg.Any<byte[]>());
    }

    [Fact]
    public async Task SetAccountIdAndSendKey_WhenAllValidationsPass()
    {
        var worldKey = new byte[32];
        var publicKey = new byte[ValidKeySize];
        new Random().NextBytes(worldKey);
        new Random().NextBytes(publicKey);

        var expectedCacheKey = $"world:{_world.Id}:keys:{Convert.ToBase64String(worldKey)}";
        _cache.GetAsync(expectedCacheKey).Returns("42");
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

        _cache.GetAsync(Arg.Any<string>()).Returns("42");
        _accountRepository.FindByIdAsync(Arg.Any<AccountId>()).Returns(MakeAccount(42));

        await _handler.ExecuteAsync(MakeCtx(worldKey, publicKey));

        await _cache.Received(1).RemoveAsync($"account:42:inWorld");
    }
}
