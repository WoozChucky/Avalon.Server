using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using Avalon.Api.Authentication;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Controllers;
using Avalon.Api.Services;
using Avalon.Api.Worlds;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.GameAuth;
using Avalon.Infrastructure.Login;
using Avalon.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.GameAuth;

public sealed class SteamWebLinksControllerShould
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
    private readonly AtomicAuthStore _memory = new();
    private readonly Account _root = new() { Id = new AccountId(7), Username = "PLAYER", Email = "player@example.test", JoinDate = DateTime.UnixEpoch, Salt = [1], Verifier = [2] };
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IExternalIdentityRepository _identities = Substitute.For<IExternalIdentityRepository>();
    private readonly IAccountConsolidationRepository _operations = Substitute.For<IAccountConsolidationRepository>();
    private readonly IReauthentication _password = Substitute.For<IReauthentication>();
    private readonly SteamWebLinkStore _links;
    private readonly SteamWebLinksController _controller;
    private readonly DefaultHttpContext _http = new();
    public SteamWebLinksControllerShould()
    {
        _links = new(_memory, new GameAuthCryptography(new byte[32]), _clock);
        _accounts.FindByIdAsync(_root.Id, false, Arg.Any<CancellationToken>()).Returns(_root);
        var mfa = Substitute.For<IMfaSetupRepository>(); var hashes = Substitute.For<IMFAHashService>();
        var policy = new MfaLoginPolicy(_accounts, Substitute.For<IReplicatedCache>(), Substitute.For<ILoginLimits>(),
            Substitute.For<IMFAService>(), hashes, NullLoggerFactory.Instance);
        var recent = new AccountLinkReauthentication(_password, mfa, hashes, policy);
        _password.RequireCurrentPasswordAsync(_root.Id, "correct", IPAddress.Loopback, Arg.Any<CancellationToken>()).Returns(new Reauthenticated(_root.Id, 0));
        var coordinator = new AccountConsolidationService(_operations, Substitute.For<IWorldRepository>(), Substitute.For<IWorldDatabases>(), Substitute.For<IWorldRepositories>());
        var auth = new AuthContext(); auth.Load(_root);
        _controller = new(_links, recent, coordinator, _operations, _identities, _accounts, mfa, auth,
            Options.Create(new SteamWebLinkOptions { CallbackUrl = "https://api.example.test/account/links/steam/callback", SiteUrl = "https://web.example.test" }), _clock);
        _http.Request.Scheme = "https"; _http.Connection.RemoteIpAddress = IPAddress.Loopback;
        _http.User = new(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Jti, "browser")], "test"));
        _controller.ControllerContext = new() { HttpContext = _http };
    }
    private async Task<SteamWebLinkStart> Transaction(bool verify = false)
    {
        var transaction = (await _links.StartAsync(Guid.NewGuid(), _root, "browser", default))!;
        _http.Request.Headers.Cookie = SteamWebLinkRegistration.CookieName(transaction.Id) + "=" + transaction.Cookie;
        if (verify)
        {
            Assert.True(await _links.ChallengeAsync(transaction.Id, transaction.Cookie, default));
            Assert.True(await _links.VerifyAsync(transaction.Id, transaction.Cookie, _root, "https://steamcommunity.com/openid/id/76561198000000001", "2026-10-04T12:00:00Znonce", default));
        }
        return transaction;
    }

    [Theory]
    [InlineData("http")]
    [InlineData("pat")]
    [InlineData("launcher")]
    public async Task Reject_noninteractive_clients_before_creating_a_browser_transaction(string client)
    {
        if (client == "http") _http.Request.Scheme = "http";
        else ((ClaimsIdentity)_http.User.Identity!).AddClaim(new Claim(client == "pat" ? "pat_id" : JwtUtils.LauncherFamilyClaim, "present"));
        Assert.IsType<UnauthorizedResult>(await _controller.Start(Guid.NewGuid(), default));
        Assert.Empty(_memory.Entries);
    }
    [Fact]
    public async Task Honor_expired_temporary_locks_consistently_when_starting_and_challenging()
    {
        _root.Locked = true; _root.LockedUntil = _clock.GetUtcNow().UtcDateTime.AddSeconds(-1);
        var transaction = await Transaction();
        Assert.IsType<ChallengeResult>(await _controller.Challenge(transaction.Id, default));
    }
    [Fact]
    public async Task Require_explicit_consent_before_password_checks_or_identity_mutations()
    {
        var transaction = await Transaction(true);
        var refused = Assert.IsType<BadRequestObjectResult>(await _controller.Confirm(new(transaction.Id, "correct", null, false), Guid.NewGuid(), default));
        Assert.Equal("CONFIRMATION_REQUIRED", Assert.IsType<SteamWebLinkReply>(refused.Value).Error);
        await _password.DidNotReceiveWithAnyArgs().RequireCurrentPasswordAsync(default!, default!, default!, default);
        await _identities.DidNotReceiveWithAnyArgs().LinkWithAuthorityAsync(default!, default, default);
    }
    [Fact]
    public async Task Recover_exact_link_commit_after_epoch_change_without_rechecking_password_or_consuming_MFA()
    {
        var transaction = await Transaction(true); var request = Guid.NewGuid();
        var identity = new ExternalIdentity { Id = transaction.Id, AccountId = _root.Id, Provider = "steam", ProviderSubject = "76561198000000001" };
        _identities.LinkWithAuthorityAsync(Arg.Any<IdentityLinkOperation>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(new IdentityLinkResult(IdentityLinkStatus.Linked, identity));
        var first = Assert.IsType<OkObjectResult>(await _controller.Confirm(new(transaction.Id, "correct", null, true), request, default));
        Assert.Equal("linked", Assert.IsType<SteamWebLinkReply>(first.Value).State);
        _root.SessionEpoch++; _clock.Advance(TimeSpan.FromMinutes(3));
        _identities.FindAsync("steam", identity.ProviderSubject, Arg.Any<CancellationToken>()).Returns(identity);
        var retry = Assert.IsType<OkObjectResult>(await _controller.Confirm(new(transaction.Id, string.Empty, null, true), request, default));
        Assert.Equal("linked", Assert.IsType<SteamWebLinkReply>(retry.Value).State);
        await _password.Received(1).RequireCurrentPasswordAsync(_root.Id, "correct", IPAddress.Loopback, Arg.Any<CancellationToken>());
        await _identities.Received(1).LinkWithAuthorityAsync(Arg.Is<IdentityLinkOperation>(op => op.AccountId == _root.Id && op.SessionEpoch == 0 && op.OperationId == transaction.Id), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        Assert.IsType<BadRequestObjectResult>(await _controller.Confirm(new(transaction.Id, string.Empty, null, true), Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Require_separate_transfer_consent_if_Steam_now_belongs_to_an_automatic_root()
    {
        var transaction = await Transaction(true);
        var other = new ExternalIdentity { Id = Guid.NewGuid(), AccountId = new AccountId(9), Provider = "steam", ProviderSubject = "76561198000000001" };
        _identities.FindAsync("steam", other.ProviderSubject, Arg.Any<CancellationToken>()).Returns(other);
        var refused = Assert.IsType<BadRequestObjectResult>(await _controller.Confirm(new(transaction.Id, "correct", null, true), Guid.NewGuid(), default));
        Assert.Equal("CONSOLIDATION_CONFIRMATION_REQUIRED", Assert.IsType<SteamWebLinkReply>(refused.Value).Error);
        Assert.Equal("verified", (await _links.ReadBoundAsync(transaction.Id, _root.Id, "browser", transaction.Cookie, default))!.State);
        await _password.DidNotReceiveWithAnyArgs().RequireCurrentPasswordAsync(default!, default!, default!, default);
    }
    [Fact]
    public async Task Refuse_transfer_when_a_native_creation_races_the_consent_commit()
    {
        var transaction = await Transaction(true);
        var other = new ExternalIdentity { Id = Guid.NewGuid(), AccountId = new AccountId(9), Provider = "steam", ProviderSubject = "76561198000000001" };
        _identities.FindAsync("steam", other.ProviderSubject, Arg.Any<CancellationToken>()).Returns((ExternalIdentity?)null, other);
        _identities.LinkWithAuthorityAsync(Arg.Any<IdentityLinkOperation>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(new IdentityLinkResult(IdentityLinkStatus.SubjectTaken, other));
        var refused = Assert.IsType<BadRequestObjectResult>(await _controller.Confirm(new(transaction.Id, "correct", null, true), Guid.NewGuid(), default));
        Assert.Equal("STEAM_LINK_CHANGED_START_AGAIN", Assert.IsType<SteamWebLinkReply>(refused.Value).Error);
        await _operations.DidNotReceiveWithAnyArgs().BeginAsync(default!, default);
    }
}
