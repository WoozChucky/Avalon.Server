using Avalon.Common.Accounts;
using Avalon.Common.Cryptography;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Server.Auth;
using Avalon.Server.Auth.Configuration;
using Avalon.Server.Auth.Handlers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using ProtoBuf;
using AvalonWorld = Avalon.Domain.Auth.World;

namespace Avalon.Server.Auth.UnitTests.Handlers;

public class CWorldSelectHandlerShould
{
    private readonly IAccountRepository _accountRepository = Substitute.For<IAccountRepository>();
    private readonly IWorldRepository _worldRepository = Substitute.For<IWorldRepository>();
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();
    private readonly IAuthConnection _connection = Substitute.For<IAuthConnection>();
    private readonly IAvalonCryptoSession _cryptoSession = new FakeAvalonCryptoSession();
    private readonly ISecureRandom _secureRandom = Substitute.For<ISecureRandom>();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero));
    private readonly CapturingLogger _logger = new();
    private readonly CWorldSelectHandler _handler;

    public CWorldSelectHandlerShould()
    {
        _connection.CryptoSession.Returns(_cryptoSession);
        _connection.Id.Returns(Guid.NewGuid());
        _connection.WorldSelects.Returns(new WorldSelectBudget());
        _secureRandom.GetBytes(32).Returns(new byte[32]);
        _cache.SetNxAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>()).Returns(true);
        _handler = Handler(new AuthConfiguration().MaxWorldSelectsPerMinute);
    }

    private CWorldSelectHandler Handler(int maxSelectsPerMinute)
    {
        var loggerFactory = Substitute.For<ILoggerFactory>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(_logger);
        return new CWorldSelectHandler(loggerFactory, _cache, _accountRepository, _worldRepository, _secureRandom,
            Options.Create(new AuthConfiguration { MaxWorldSelectsPerMinute = maxSelectsPerMinute }), _clock);
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

    /// <summary>
    /// #554: an unknown world is answered with WorldUnavailable rather than silence, so the client
    /// is told at once. No key, no mutex, and the connection stays open to pick another world.
    /// </summary>
    [Fact]
    public async Task Answer_WorldUnavailable_WhenWorldNotFound()
    {
        var account = MakeAccount();
        _connection.AccountId.Returns(account.Id);
        _accountRepository.FindByIdAsync(account.Id).Returns(account);
        _worldRepository.FindByIdAsync(Arg.Any<WorldId>()).Returns((AvalonWorld?)null);

        await _handler.ExecuteAsync(new AuthPacketContext<CWorldSelectPacket>
        {
            Packet = new CWorldSelectPacket { WorldId = new WorldId(99) },
            Connection = _connection
        });

        SWorldSelectPacket sent = Assert.Single(SentPackets());
        Assert.Equal(WorldSelectResult.WorldUnavailable, sent.Result);
        Assert.Empty(sent.WorldKey);
        await AssertRefusedWithoutSideEffectsAsync();
    }

    /// <summary>
    /// #554: a world the account may not enter gets the very same answer as one that does not
    /// exist, so a tampered client cannot probe for restricted worlds.
    /// </summary>
    [Fact]
    public async Task Answer_WorldUnavailable_WhenAccountLacksRequiredAccessLevel()
    {
        var account = MakeAccount(level: AccountAccessLevel.Player);
        _connection.AccountId.Returns(account.Id);
        _accountRepository.FindByIdAsync(account.Id).Returns(account);
        var world = MakeWorld(req: AccountAccessLevel.Admin);
        _worldRepository.FindByIdAsync(Arg.Any<WorldId>()).Returns(world);

        await _handler.ExecuteAsync(new AuthPacketContext<CWorldSelectPacket>
        {
            Packet = new CWorldSelectPacket { WorldId = new WorldId(1) },
            Connection = _connection
        });

        SWorldSelectPacket sent = Assert.Single(SentPackets());
        Assert.Equal(WorldSelectResult.WorldUnavailable, sent.Result);
        Assert.Empty(sent.WorldKey);
        await AssertRefusedWithoutSideEffectsAsync();
    }

    /// <summary>
    /// #554: the two refusals must be indistinguishable on the wire. The fake crypto session is a
    /// pass-through, so the packets compare byte for byte, header and payload alike. PTR (32) and
    /// Tournament (16) are numerically above Admin (4), so these cases fail under an ordinal check.
    /// </summary>
    [Theory]
    [InlineData(AccountAccessLevel.Admin, AccountAccessLevel.Player)]
    [InlineData(AccountAccessLevel.Admin, AccountAccessLevel.PTR)]
    [InlineData(AccountAccessLevel.Admin, AccountAccessLevel.Tournament)]
    public async Task Answer_An_Unknown_World_And_A_Forbidden_World_Identically(AccountAccessLevel required, AccountAccessLevel level)
    {
        var account = MakeAccount(level: level);
        _connection.AccountId.Returns(account.Id);
        _accountRepository.FindByIdAsync(account.Id).Returns(account);
        var forbidden = MakeWorld(1, required);
        _worldRepository.FindByIdAsync(forbidden.Id).Returns(forbidden);
        _worldRepository.FindByIdAsync(new WorldId(99)).Returns((AvalonWorld?)null);

        await _handler.ExecuteAsync(new AuthPacketContext<CWorldSelectPacket>
        {
            Packet = new CWorldSelectPacket { WorldId = new WorldId(99) },
            Connection = _connection
        });
        await _handler.ExecuteAsync(new AuthPacketContext<CWorldSelectPacket>
        {
            Packet = new CWorldSelectPacket { WorldId = forbidden.Id },
            Connection = _connection
        });

        List<NetworkPacket> sent = SentNetworkPackets();
        Assert.Equal(2, sent.Count);
        Assert.Equal(sent[0].Header.Type, sent[1].Header.Type);
        Assert.Equal(sent[0].Header.Flags, sent[1].Header.Flags);
        Assert.Equal(sent[0].Header.Protocol, sent[1].Header.Protocol);
        Assert.Equal(sent[0].Header.Version, sent[1].Header.Version);
        Assert.Equal(sent[0].Payload, sent[1].Payload);

        SWorldSelectPacket unknown = Decode(sent[0]);
        SWorldSelectPacket refused = Decode(sent[1]);
        Assert.Equal(WorldSelectResult.WorldUnavailable, unknown.Result);
        Assert.Equal(unknown.Result, refused.Result);
        Assert.Equal(unknown.WorldKey, refused.WorldKey);
        await AssertRefusedWithoutSideEffectsAsync();
    }

    private async Task AssertRefusedWithoutSideEffectsAsync()
    {
        _connection.DidNotReceive().Close();
        _secureRandom.DidNotReceiveWithAnyArgs().GetBytes(default);
        await _cache.DidNotReceiveWithAnyArgs().SetNxAsync(default!, default!, default);
        await _cache.DidNotReceiveWithAnyArgs().SetAsync(default!, default!, default);
        await _cache.DidNotReceiveWithAnyArgs().PublishAsync(default!, default!);
        await _accountRepository.DidNotReceiveWithAnyArgs().SetSessionKeyAsync(default!, default!, default);
    }

    private List<NetworkPacket> SentNetworkPackets()
        => _connection.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IAuthConnection.Send))
            .Select(c => c.GetArguments()[0])
            .OfType<NetworkPacket>()
            .ToList();

    private List<SWorldSelectPacket> SentPackets() => SentNetworkPackets().Select(Decode).ToList();

    // FakeAvalonCryptoSession.Encrypt is a pass-through, so the payload is the plain protobuf.
    private static SWorldSelectPacket Decode(NetworkPacket packet)
    {
        Assert.Equal(NetworkPacketType.SMSG_WORLD_SELECT, packet.Header.Type);
        using var stream = new MemoryStream(packet.Payload);
        return Serializer.Deserialize<SWorldSelectPacket>(stream);
    }

    /// <summary>
    /// #447: select used an ordinal "required &gt; actual" check, so a PTR or Tournament account
    /// could enter the Admin-only world. Refusal is observable as no session slot being taken.
    /// </summary>
    [Theory]
    [InlineData(AccountAccessLevel.Admin, AccountAccessLevel.PTR)]
    [InlineData(AccountAccessLevel.Admin, AccountAccessLevel.Tournament)]
    [InlineData(AccountAccessLevel.Admin, AccountAccessLevel.Player | AccountAccessLevel.PTR)]
    [InlineData(AccountAccessLevel.PTR, AccountAccessLevel.Player)]
    [InlineData(AccountAccessLevel.Tournament, AccountAccessLevel.PTR)]
    public async Task Refuse_A_World_The_Account_May_Not_Enter(AccountAccessLevel required, AccountAccessLevel level)
    {
        await SelectAsync(required, level);

        await _cache.DidNotReceiveWithAnyArgs().SetNxAsync(default!, default!, default);
        Assert.Equal(WorldSelectResult.WorldUnavailable, Assert.Single(SentPackets()).Result);
        _connection.DidNotReceive().Close();
    }

    /// <summary>Staff reach a PTR world without holding the PTR flag; PTR testers reach it too.</summary>
    [Theory]
    [InlineData(AccountAccessLevel.PTR, AccountAccessLevel.PTR)]
    [InlineData(AccountAccessLevel.PTR, AccountAccessLevel.Player | AccountAccessLevel.GameMaster | AccountAccessLevel.Admin)]
    [InlineData(AccountAccessLevel.Player, AccountAccessLevel.Tournament)]
    public async Task Admit_A_World_The_Account_May_Enter(AccountAccessLevel required, AccountAccessLevel level)
    {
        await SelectAsync(required, level);

        await _cache.ReceivedWithAnyArgs(1).SetNxAsync(default!, default!, default);
    }

    private async Task SelectAsync(AccountAccessLevel required, AccountAccessLevel level)
    {
        var account = MakeAccount(level: level);
        _connection.AccountId.Returns(account.Id);
        _accountRepository.FindByIdAsync(account.Id).Returns(account);
        var world = MakeWorld(1, required);
        _worldRepository.FindByIdAsync(world.Id).Returns(world);

        await _handler.ExecuteAsync(new AuthPacketContext<CWorldSelectPacket>
        {
            Packet = new CWorldSelectPacket { WorldId = world.Id },
            Connection = _connection
        });
    }

    /// <summary>
    /// #495: the connection logged in at version 0, and a password change has since moved the
    /// account to 1. The connection proved credentials that no longer hold: no key, no slot.
    /// </summary>
    [Fact]
    public async Task CloseConnection_WhenTheCredentialsChangedSinceItsLogin()
    {
        var account = MakeAccount();
        account.CredentialsVersion = 1;
        _connection.AccountId.Returns(account.Id);
        _connection.CredentialsVersion.Returns(0);
        _accountRepository.FindByIdAsync(account.Id).Returns(account);
        _worldRepository.FindByIdAsync(Arg.Any<WorldId>()).Returns(MakeWorld());

        await _handler.ExecuteAsync(new AuthPacketContext<CWorldSelectPacket>
        {
            Packet = new CWorldSelectPacket { WorldId = new WorldId(1) },
            Connection = _connection
        });

        _connection.Received(1).Close();
        await _cache.DidNotReceiveWithAnyArgs().SetNxAsync(default!, default!, default);
        await _cache.DidNotReceiveWithAnyArgs().SetAsync(default!, default!, default);
    }

    [Fact]
    public async Task StoreTheConnectionsCredentialsVersionWithTheWorldKey()
    {
        var account = MakeAccount();
        account.CredentialsVersion = 3;
        _connection.AccountId.Returns(account.Id);
        _connection.CredentialsVersion.Returns(3);
        _accountRepository.FindByIdAsync(account.Id).Returns(account);
        _worldRepository.FindByIdAsync(Arg.Any<WorldId>()).Returns(MakeWorld());

        await _handler.ExecuteAsync(new AuthPacketContext<CWorldSelectPacket>
        {
            Packet = new CWorldSelectPacket { WorldId = new WorldId(1) },
            Connection = _connection
        });

        await _cache.Received(1).SetAsync(Arg.Any<string>(), "5:3", Arg.Any<TimeSpan?>());
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
        // Only the key, never the whole row (#484).
        await _accountRepository.Received(1).SetSessionKeyAsync(account.Id, account.SessionKey, Arg.Any<CancellationToken>());
        await _accountRepository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
        await _cache.Received(1).SetNxAsync(
            Arg.Is<string>(k => k == $"account:{account.Id}:inWorld"),
            "1",
            TimeSpan.FromMinutes(5));
        await _cache.Received(1).SetAsync(
            Arg.Is<string>(k => k.StartsWith($"world:{world.Id}:keys:")),
            Arg.Any<string>(),
            Arg.Is<TimeSpan?>(t => t == TimeSpan.FromMinutes(5)));
        await _cache.Received(1).PublishAsync(
            Arg.Is<string>(k => k == $"world:{world.Id}:select"),
            Arg.Is<string>(v => v.StartsWith($"account:{account.Id}:worldKey:")));
    }

    [Fact]
    public async Task UseSecureRandom_ForWorldKey_WhenSelectionSucceeds()
    {
        var expectedKey = new byte[32];
        new System.Random(42).NextBytes(expectedKey); // deterministic stand-in — not used as CSPRNG
        _secureRandom.GetBytes(32).Returns(expectedKey);

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

        _secureRandom.Received(1).GetBytes(32);
        Assert.Equal(expectedKey, account.SessionKey);
        SWorldSelectPacket sent = Assert.Single(SentPackets());
        Assert.Equal(WorldSelectResult.Success, sent.Result);
        Assert.Equal(expectedKey, sent.WorldKey);
        _connection.DidNotReceive().Close();
    }

    [Fact]
    public async Task SendDuplicateSessionError_WhenAccountAlreadyHasActiveSession()
    {
        _cache.SetNxAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>()).Returns(false);

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

        _connection.Received(1).Send(Arg.Any<NetworkPacket>());
        SWorldSelectPacket sent = Assert.Single(SentPackets());
        Assert.Equal(WorldSelectResult.DuplicateSession, sent.Result);
        Assert.Empty(sent.WorldKey);
        _connection.DidNotReceive().Close();
        await _accountRepository.DidNotReceiveWithAnyArgs().SetSessionKeyAsync(default!, default!, default);
        await _cache.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>());
        await _cache.DidNotReceive().PublishAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    /// <summary>
    /// #462: defence in depth behind the login check. An account banned or deactivated after it
    /// logged in must not take the inWorld slot or be issued a world key.
    /// </summary>
    [Theory]
    [InlineData(AccountStatus.Banned)]
    [InlineData(AccountStatus.Deactivated)]
    public async Task Refuse_A_World_Key_To_An_Account_That_Is_Not_Active(AccountStatus status)
    {
        var account = MakeAccount(level: AccountAccessLevel.Player);
        account.Status = status;
        _connection.AccountId.Returns(account.Id);
        _accountRepository.FindByIdAsync(account.Id).Returns(account);
        var world = MakeWorld(1, AccountAccessLevel.Player);
        _worldRepository.FindByIdAsync(Arg.Any<WorldId>()).Returns(world);

        await _handler.ExecuteAsync(new AuthPacketContext<CWorldSelectPacket>
        {
            Packet = new CWorldSelectPacket { WorldId = world.Id },
            Connection = _connection
        });

        _secureRandom.DidNotReceiveWithAnyArgs().GetBytes(default);
        Assert.Empty(account.SessionKey);
        _connection.DidNotReceiveWithAnyArgs().Send(default!);
        await _cache.DidNotReceiveWithAnyArgs().SetNxAsync(default!, default!, default);
        await _cache.DidNotReceiveWithAnyArgs().SetAsync(default!, default!, default);
        await _cache.DidNotReceiveWithAnyArgs().PublishAsync(default!, default!);
        await _accountRepository.DidNotReceiveWithAnyArgs().SetSessionKeyAsync(default!, default!, default);
        _connection.Received(1).Close();
    }

    private const string BudgetWarning = "world select budget";

    private Account LoggedIn()
    {
        var account = MakeAccount();
        _connection.AccountId.Returns(account.Id);
        _accountRepository.FindByIdAsync(account.Id).Returns(account);
        _worldRepository.FindByIdAsync(Arg.Any<WorldId>()).Returns(MakeWorld());
        return account;
    }

    private Task SelectWith(CWorldSelectHandler handler, ushort worldId = 1)
        => handler.ExecuteAsync(new AuthPacketContext<CWorldSelectPacket>
        {
            Packet = new CWorldSelectPacket { WorldId = new WorldId(worldId) },
            Connection = _connection
        });

    private int WarningsContaining(string text)
        => _logger.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains(text, StringComparison.Ordinal));

    /// <summary>
    /// #574: the select past the cap closes the connection, and is counted before anything is
    /// read, so it costs no database read, no cache call and no reply.
    /// </summary>
    [Fact]
    public async Task Close_the_connection_on_the_select_past_the_cap_without_reading_anything()
    {
        LoggedIn();
        CWorldSelectHandler handler = Handler(3);
        for (int i = 0; i < 3; i++)
            await SelectWith(handler);
        _connection.DidNotReceive().Close();
        _accountRepository.ClearReceivedCalls();
        _worldRepository.ClearReceivedCalls();
        _cache.ClearReceivedCalls();
        _secureRandom.ClearReceivedCalls();
        _connection.ClearReceivedCalls();

        await SelectWith(handler);

        _connection.Received(1).Close();
        _connection.DidNotReceiveWithAnyArgs().Send(default!);
        Assert.Empty(_accountRepository.ReceivedCalls());
        Assert.Empty(_worldRepository.ReceivedCalls());
        Assert.Empty(_cache.ReceivedCalls());
        Assert.Empty(_secureRandom.ReceivedCalls());
    }

    /// <summary>#574: every select up to the cap is answered as before: Success, DuplicateSession or WorldUnavailable.</summary>
    [Theory]
    [InlineData(WorldSelectResult.Success)]
    [InlineData(WorldSelectResult.DuplicateSession)]
    [InlineData(WorldSelectResult.WorldUnavailable)]
    public async Task Answer_every_select_up_to_the_cap_as_before(WorldSelectResult expected)
    {
        LoggedIn();
        if (expected == WorldSelectResult.DuplicateSession)
            _cache.SetNxAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>()).Returns(false);
        ushort worldId = expected == WorldSelectResult.WorldUnavailable ? (ushort)99 : (ushort)1;
        if (expected == WorldSelectResult.WorldUnavailable)
            _worldRepository.FindByIdAsync(new WorldId(99)).Returns((AvalonWorld?)null);
        CWorldSelectHandler handler = Handler(10);

        for (int i = 0; i < 10; i++)
            await SelectWith(handler, worldId);

        List<SWorldSelectPacket> sent = SentPackets();
        Assert.Equal(10, sent.Count);
        Assert.All(sent, p => Assert.Equal(expected, p.Result));
        _connection.DidNotReceive().Close();
        Assert.Equal(0, WarningsContaining(BudgetWarning));
    }

    /// <summary>#574: the window is fixed from its first select; once it has ended the count starts again.</summary>
    [Fact]
    public async Task Start_the_count_again_once_the_window_has_ended()
    {
        LoggedIn();
        CWorldSelectHandler handler = Handler(2);
        await SelectWith(handler);
        _clock.Advance(TimeSpan.FromSeconds(30));
        await SelectWith(handler);
        _clock.Advance(TimeSpan.FromSeconds(30));
        _connection.ClearReceivedCalls();

        await SelectWith(handler);

        _connection.DidNotReceive().Close();
        Assert.Equal(WorldSelectResult.Success, Assert.Single(SentPackets()).Result);
    }

    /// <summary>#574: a moment before the window ends, the select past the cap is still refused.</summary>
    [Fact]
    public async Task Refuse_the_select_past_the_cap_until_the_window_has_ended()
    {
        LoggedIn();
        CWorldSelectHandler handler = Handler(2);
        await SelectWith(handler);
        await SelectWith(handler);
        _clock.Advance(TimeSpan.FromMinutes(1) - TimeSpan.FromMilliseconds(1));

        await SelectWith(handler);

        _connection.Received(1).Close();
    }

    /// <summary>
    /// #574: selects keep arriving after the close until the connection is gone; the refusal is
    /// logged at Warning once per window, not once per select.
    /// </summary>
    [Fact]
    public async Task Log_the_budget_refusal_at_most_once_per_window()
    {
        LoggedIn();
        CWorldSelectHandler handler = Handler(1);
        await SelectWith(handler);

        for (int i = 0; i < 5; i++)
            await SelectWith(handler);
        Assert.Equal(1, WarningsContaining(BudgetWarning));

        _clock.Advance(TimeSpan.FromMinutes(1));
        for (int i = 0; i < 5; i++)
            await SelectWith(handler);
        Assert.Equal(2, WarningsContaining(BudgetWarning));
    }

    /// <summary>#574: the WorldUnavailable refusal is also logged at Warning once per window, whichever of the two it is.</summary>
    [Fact]
    public async Task Log_the_world_unavailable_refusal_at_most_once_per_window()
    {
        LoggedIn();
        _worldRepository.FindByIdAsync(new WorldId(99)).Returns((AvalonWorld?)null);
        _worldRepository.FindByIdAsync(new WorldId(2)).Returns(MakeWorld(2, AccountAccessLevel.Admin));
        CWorldSelectHandler handler = Handler(10);

        for (int i = 0; i < 4; i++)
            await SelectWith(handler, i % 2 == 0 ? (ushort)99 : (ushort)2);
        Assert.Equal(1, _logger.Count(LogLevel.Warning));

        _clock.Advance(TimeSpan.FromMinutes(1));
        await SelectWith(handler, 2);
        Assert.Equal(2, _logger.Count(LogLevel.Warning));
        Assert.Equal(5, SentPackets().Count);
        Assert.All(SentPackets(), p => Assert.Equal(WorldSelectResult.WorldUnavailable, p.Result));
    }
}
