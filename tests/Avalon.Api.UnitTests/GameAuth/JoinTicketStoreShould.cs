using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.GameAuth;
using Avalon.Infrastructure.StoreAuth;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.GameAuth;

public sealed class JoinTicketStoreShould
{
    private readonly JoinHarness _h = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shared_revocation_refuses_exact_join_issue_and_redemption_receipts(bool native)
    {
        var auth = native ? await _h.AuthenticateAvalon() : await _h.Authenticate();
        var request = Guid.NewGuid(); var connection = Guid.NewGuid(); var redemption = Guid.NewGuid();
        var issued = await _h.Tickets.IssueAsync(auth.GameContextCredential!, 1, null, request, false, false, default);
        Assert.Null(issued.Error);
        var receipt = await _h.Tickets.RedeemAsync(issued.JoinTicket!, "world-1", connection, redemption, default);
        Assert.Null(receipt.Error);
        _h.RevokeLicense();
        Assert.NotNull((await _h.Tickets.IssueAsync(auth.GameContextCredential!, 1, null, request, false, false, default)).Error);
        Assert.NotNull((await _h.Tickets.RedeemAsync(issued.JoinTicket!, "world-1", connection, redemption, default)).Error);
        await _h.Sessions.Received(1).TryReserveAsync(Arg.Any<GameSessionReservation>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Playtest_cannot_issue_other_worlds_or_replay_receipts_after_restriction_changes()
    {
        _h.Account.AccessLevel |= Avalon.Common.Accounts.AccountAccessLevel.Admin;
        _h.Allocator.FindAsync(Arg.Any<GameContextRecord>(), 3, Arg.Any<uint?>(), Arg.Any<CancellationToken>()).Returns(new GameWorldDestination(3, "world-3", "PTR", "localhost", 21000, "localhost", new string('A', 64), "0.2.0", "0.2.0"));
        _h.Allocator.FindAsync(Arg.Any<GameContextRecord>(), 2, Arg.Any<uint?>(), Arg.Any<CancellationToken>()).Returns(new GameWorldDestination(2, "world-2", "Asthoria", "localhost", 21000, "localhost", new string('A', 64), "0.2.0", "0.2.0"));
        var auth = await _h.Authenticate(2514590);
        foreach (ushort world in new ushort[] { 1, 2 })
            Assert.NotNull((await _h.Tickets.IssueAsync(auth.GameContextCredential!, world, null, Guid.NewGuid(), false, false, default)).Error);
        var request = Guid.NewGuid();
        var issued = await _h.Tickets.IssueAsync(auth.GameContextCredential!, 3, null, request, false, false, default);
        Assert.Null(issued.Error);
        Assert.Equal(issued, await _h.Tickets.IssueAsync(auth.GameContextCredential!, 3, null, request, false, false, default));
        _h.Configuration.SteamPlaytest.AllowedWorldIds = [1];
        Assert.NotNull((await _h.Tickets.IssueAsync(auth.GameContextCredential!, 3, null, request, false, false, default)).Error);
        Assert.NotNull((await _h.Tickets.RedeemAsync(issued.JoinTicket!, "world-3", Guid.NewGuid(), Guid.NewGuid(), default)).Error);
        await _h.Sessions.DidNotReceive().TryReserveAsync(Arg.Any<GameSessionReservation>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        _h.Configuration.SteamPlaytest.AllowedWorldIds = [3];
        var connection = Guid.NewGuid(); var redemption = Guid.NewGuid();
        var receipt = await _h.Tickets.RedeemAsync(issued.JoinTicket!, "world-3", connection, redemption, default);
        Assert.Null(receipt.Error);
        Assert.Equal(receipt, await _h.Tickets.RedeemAsync(issued.JoinTicket!, "world-3", connection, redemption, default));
        _h.Configuration.SteamPlaytest.AllowedWorldIds = [1];
        Assert.NotNull((await _h.Tickets.RedeemAsync(issued.JoinTicket!, "world-3", connection, redemption, default)).Error);
        _h.Configuration.SteamPlaytest.AllowedWorldIds = [3];
        _h.Configuration.SteamPlaytest.Enabled = false;
        Assert.NotNull((await _h.Tickets.RedeemAsync(issued.JoinTicket!, "world-3", connection, redemption, default)).Error);
        Assert.NotNull((await _h.Tickets.IssueAsync(auth.GameContextCredential!, 3, null, request, false, false, default)).Error);
    }

    [Fact]
    public async Task Cap_a_one_use_ticket_to_credentials_context_and_license_and_recover_issuance()
    {
        var auth = await _h.Authenticate();
        _h.Clock.Advance(TimeSpan.FromMinutes(4).Add(TimeSpan.FromSeconds(50)));
        var request = Guid.NewGuid();
        var reply = await _h.Tickets.IssueAsync(auth.GameContextCredential!, 1, null, request, false, false, CancellationToken.None);
        Assert.Null(reply.Error);
        Assert.Equal(_h.Clock.GetUtcNow().UtcDateTime.AddSeconds(10), reply.ExpiresAt);
        Assert.Equal(reply, await _h.Tickets.IssueAsync(auth.GameContextCredential!, 1, null, request, false, false, CancellationToken.None));
        Assert.DoesNotContain(reply.JoinTicket!, string.Join("|", _h.Store.Entries.Keys.Concat(_h.Store.Entries.Values)), StringComparison.Ordinal);
        Assert.DoesNotContain(reply.JoinTicket!, reply.ToString(), StringComparison.Ordinal);
        _h.Clock.Advance(TimeSpan.FromSeconds(10));
        Assert.NotNull((await _h.Tickets.RedeemAsync(reply.JoinTicket!, "world-1", Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None)).Error);
    }

    [Fact]
    public async Task Consume_for_one_connection_and_return_only_exact_authenticated_redemption_retries()
    {
        var auth = await _h.Authenticate();
        var reply = await _h.Tickets.IssueAsync(auth.GameContextCredential!, 1, null, Guid.NewGuid(), false, false, CancellationToken.None);
        Assert.Equal(_h.Clock.GetUtcNow().UtcDateTime.AddSeconds(30), reply.ExpiresAt);
        var attempts = await Task.WhenAll(Enumerable.Range(0, 256).Select(_ =>
            _h.Tickets.RedeemAsync(reply.JoinTicket!, "world-1", Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None)));
        var winner = Assert.Single(attempts.Where(r => r.Error is null));
        Assert.Equal("7", winner.AccountId);
        Assert.Equal("1", winner.FencingToken);
        Assert.NotNull(winner.GameSessionId);
        Assert.NotNull(winner.ConnectionId);
        Assert.NotNull(winner.RedemptionId);
        var receipt = await _h.Tickets.RedeemAsync(reply.JoinTicket!, "world-1", Guid.Parse(winner.ConnectionId!), Guid.Parse(winner.RedemptionId!), CancellationToken.None);
        Assert.Equal(winner, receipt);
        Assert.NotNull((await _h.Tickets.RedeemAsync(reply.JoinTicket!, "world-2", Guid.Parse(winner.ConnectionId!), Guid.Parse(winner.RedemptionId!), CancellationToken.None)).Error);
        await _h.Sessions.Received(1).TryReserveAsync(Arg.Any<GameSessionReservation>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reject_the_wrong_server_without_consuming_the_ticket_and_revalidate_account_authority()
    {
        var auth = await _h.Authenticate();
        var issued = await _h.Tickets.IssueAsync(auth.GameContextCredential!, 1, 42, Guid.NewGuid(), false, false, CancellationToken.None);
        Assert.NotNull((await _h.Tickets.RedeemAsync(issued.JoinTicket!, "world-2", Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None)).Error);
        _h.Account.SessionEpoch++;
        Assert.NotNull((await _h.Tickets.RedeemAsync(issued.JoinTicket!, "world-1", Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None)).Error);
        await _h.Sessions.DidNotReceive().TryReserveAsync(Arg.Any<GameSessionReservation>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Require_explicit_takeover_and_allow_reconnect_only_for_the_original_context()
    {
        var auth = await _h.Authenticate();
        var active = new GameSession
        {
            AccountId = _h.Account.Id, GameSessionId = Guid.NewGuid(), GameContextId = Guid.NewGuid(), FencingToken = 3,
            ServerId = "world-1", WorldId = 1, Environment = "production", State = GameSessionState.Active,
            CreatedAt = _h.Clock.GetUtcNow().UtcDateTime, LeaseUntil = _h.Clock.GetUtcNow().UtcDateTime.AddSeconds(45),
            LicenseUntil = _h.Clock.GetUtcNow().UtcDateTime.AddMinutes(5),
        };
        _h.Sessions.FindAsync(_h.Account.Id, Arg.Any<CancellationToken>()).Returns(active);
        Assert.Equal("ACTIVE_GAME_SESSION", (await _h.Tickets.IssueAsync(auth.GameContextCredential!, 1, null, Guid.NewGuid(), false, false, CancellationToken.None)).Error);
        Assert.NotNull((await _h.Tickets.IssueAsync(auth.GameContextCredential!, 1, null, Guid.NewGuid(), false, true, CancellationToken.None)).Error);
        Assert.Null((await _h.Tickets.IssueAsync(auth.GameContextCredential!, 1, null, Guid.NewGuid(), true, false, CancellationToken.None)).Error);
    }

    [Fact]
    public async Task Never_issue_for_an_unassigned_world_or_invalid_character_selection()
    {
        var auth = await _h.Authenticate();
        _h.Allocator.FindAsync(Arg.Any<GameContextRecord>(), 2, Arg.Any<uint?>(), Arg.Any<CancellationToken>()).Returns((GameWorldDestination?)null);
        Assert.NotNull((await _h.Tickets.IssueAsync(auth.GameContextCredential!, 2, null, Guid.NewGuid(), false, false, CancellationToken.None)).Error);
        _h.Allocator.FindAsync(Arg.Any<GameContextRecord>(), 1, 999, Arg.Any<CancellationToken>()).Returns((GameWorldDestination?)null);
        Assert.NotNull((await _h.Tickets.IssueAsync(auth.GameContextCredential!, 1, 999, Guid.NewGuid(), false, false, CancellationToken.None)).Error);
    }
}

internal sealed class JoinHarness
{
    private readonly Guid _family = Guid.NewGuid();
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
    public AtomicAuthStore Store { get; } = new();
    public Account Account { get; } = new() { Id = new AccountId(7), Username = "PLAYER", Email = "player@example.test", Salt = [1], Verifier = [2], JoinDate = DateTime.UnixEpoch };
    public IGameSessionRepository Sessions { get; } = Substitute.For<IGameSessionRepository>();
    public IGameServerAllocator Allocator { get; } = Substitute.For<IGameServerAllocator>();
    public GameAuthorizationService Authorization { get; }
    public JoinTicketStore Tickets { get; }
    public StoreAuthenticationConfiguration Configuration { get; } = new() { SteamAppId = StoreAuthenticationTestData.SteamAppId, SteamPublisherKey = "test-only", SteamPlaytest = new() { Enabled = true, AppId = 2514590, AllowedWorldIds = [3] } };
    public JoinHarness()
    {
        var accounts = Substitute.For<IAccountRepository>();
        var identities = Substitute.For<IExternalIdentityRepository>();
        var verifier = Substitute.For<ISteamProofVerifier>();
        var ownership = Substitute.For<ISteamOwnershipClient>();
        var crypto = new GameAuthCryptography(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray());
        var options = Options.Create(Configuration);
        accounts.FindByIdAsync(Account.Id, false, Arg.Any<CancellationToken>()).Returns(Account);
        identities.FindAsync("steam", "76561198000000001", Arg.Any<CancellationToken>()).Returns(new ExternalIdentity { Id = Guid.NewGuid(), AccountId = Account.Id, Provider = "steam", ProviderSubject = "76561198000000001" });
        verifier.VerifyAsync(Arg.Any<uint>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new SteamProofResult(SteamProofStatus.Verified, "76561198000000001"));
        ownership.CheckAsync(Arg.Any<uint>(), "76561198000000001", Arg.Any<CancellationToken>()).Returns(call => new SteamOwnershipResult(SteamOwnershipStatus.Owned, "76561198000000001", Clock.GetUtcNow().UtcDateTime, Clock.GetUtcNow().UtcDateTime.AddMinutes(5)));
        var families = Substitute.For<IRefreshTokenRepository>();
        families.IsLiveLauncherFamilyAsync(Account.Id, _family, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        Authorization = TestGameAuthorization.Create(Store, new AuthAttemptStore(Store, crypto, options, Clock), crypto, accounts,
            families, identities, Substitute.For<ILicenseObservationRepository>(), verifier, ownership, options, Clock);
        Allocator.FindAsync(Arg.Any<GameContextRecord>(), 1, Arg.Any<uint?>(), Arg.Any<CancellationToken>()).Returns(new GameWorldDestination(1, "world-1", "Avalon", "localhost", 21000, "localhost", new string('A', 64), "0.0.1", "0.0.1"));
        Sessions.TryReserveAsync(Arg.Any<GameSessionReservation>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var r = call.Arg<GameSessionReservation>();
            var head = new GameSession { AccountId = r.AccountId, GameSessionId = r.GameSessionId, GameContextId = r.GameContextId,
                FencingToken = r.ExpectedFence + 1, ServerId = r.ServerId, WorldId = r.WorldId, Environment = r.Environment,
                State = GameSessionState.Pending, CredentialsVersion = r.CredentialsVersion, SessionEpoch = r.SessionEpoch,
                CreatedAt = Clock.GetUtcNow().UtcDateTime, LeaseUntil = Clock.GetUtcNow().UtcDateTime.AddSeconds(45), LicenseUntil = r.LicenseUntil };
            Sessions.FindAsync(Account.Id, Arg.Any<CancellationToken>()).Returns(head);
            return head;
        });
        Tickets = new(Store, crypto, Authorization, Sessions, Allocator, options, Clock, new GameApplicationAccessPolicy(options));
    }
    public async Task<GameAuthReply> Authenticate(uint? appId = null)
    {
        var attempt = (await Authorization.CreateAttemptAsync("steam", "1", Guid.NewGuid(), new string('A', 43), null, appId, CancellationToken.None))!;
        return await Authorization.AuthenticateSteamAsync(attempt.AttemptCredential, "ABCD", Guid.NewGuid(), CancellationToken.None);
    }
    public async Task<GameAuthReply> AuthenticateAvalon()
    {
        TestGameAuthorization.Licenses(Store).Rows.Add(new GameLicense { Id = Guid.NewGuid(), AccountId = Account.Id,
            Provider = "avalon", Environment = "production", Product = StoreAuthenticationConfiguration.Product, ProviderProductId = "base",
            LicenseReference = "native-test-grant", AuthorityKind = Avalon.Common.GameAuth.LicenseAuthorityKind.StoredGrant,
            GrantedAt = Clock.GetUtcNow().UtcDateTime });
        var attempt = (await Authorization.CreateAttemptAsync("avalon", "1", Guid.NewGuid(), new string('A', 43), null, null, default))!;
        var ticket = GameAuthCryptography.NewToken();
        Store.Seed(Avalon.Infrastructure.GameTickets.RedisGameTicketStore.Key(ticket), $"7|{_family:D}|0|0|production");
        return await Authorization.RedeemHandoffAsync(attempt.AttemptCredential, ticket, Guid.NewGuid(), default);
    }
    public void RevokeLicense()
    {
        var row = Assert.Single(TestGameAuthorization.Licenses(Store).Rows);
        row.RevokedAt = Clock.GetUtcNow().UtcDateTime; row.AuthorityRevision++;
    }
}
