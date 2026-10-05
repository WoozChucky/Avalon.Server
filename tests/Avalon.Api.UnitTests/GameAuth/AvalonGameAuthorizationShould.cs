using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.GameAuth;
using Avalon.Infrastructure.GameTickets;
using Avalon.Infrastructure.StoreAuth;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.GameAuth;

public sealed class AvalonGameAuthorizationShould
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
    private readonly AtomicAuthStore _store = new();
    private readonly MemoryGameLicenses _licenses = new();
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IRefreshTokenRepository _families = Substitute.For<IRefreshTokenRepository>();
    private readonly IExternalIdentityRepository _identities = Substitute.For<IExternalIdentityRepository>();
    private readonly ILicenseObservationRepository _observations = Substitute.For<ILicenseObservationRepository>();
    private readonly GameAuthCryptography _crypto = new(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray());
    private readonly IOptions<StoreAuthenticationConfiguration> _options = Options.Create(new StoreAuthenticationConfiguration { SteamAppId = 2499460 });
    private readonly Account _account = new() { Id = new(7), Username = "NATIVE", Email = "native@example.test", Salt = [1], Verifier = [2], JoinDate = DateTime.UnixEpoch };
    private readonly Guid _family = Guid.NewGuid();
    private readonly Guid _run = Guid.NewGuid();
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;
    public AvalonGameAuthorizationShould()
    {
        _accounts.FindByIdAsync(_account.Id, false, Arg.Any<CancellationToken>()).Returns(_account);
        _families.IsLiveLauncherFamilyAsync(_account.Id, _family, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
    }
    private GameAuthorizationService Service(IGameLicenseRepository? repository = null)
    {
        var licenses = repository ?? _licenses;
        var registry = new GameProviderRegistry([], [new AvalonLicenseProvider(licenses)]);
        return new(_store, new(_store, _crypto, _options, _clock), _crypto, _accounts, _families, _identities,
            registry, new(registry, licenses, _observations, _options), _options, _clock);
    }
    private GameLicense Grant(DateTime? expires = null, string provider = "avalon")
    {
        var row = new GameLicense { Id = Guid.NewGuid(), AccountId = _account.Id, Provider = provider, Product = StoreAuthenticationConfiguration.Product,
            Environment = "production", ProviderProductId = "base", LicenseReference = Guid.NewGuid().ToString("N"),
            AuthorityKind = LicenseAuthorityKind.StoredGrant, GrantedAt = Now, ExpiresAt = expires };
        _licenses.Rows.Add(row); return row;
    }
    private async Task<(GameAuthReply Reply, string Attempt, string Ticket, Guid Request)> Handoff(GameAuthorizationService? service = null)
    {
        service ??= Service();
        var attempt = (await service.CreateAttemptAsync("avalon", "1", _run, new string('A', 43), null, null, default))!;
        var ticket = GameAuthCryptography.NewToken(); var request = Guid.NewGuid();
        _store.Seed(RedisGameTicketStore.Key(ticket), $"7|{_family:D}|0|0|production");
        return (await service.RedeemHandoffAsync(attempt.AttemptCredential, ticket, request, default), attempt.AttemptCredential, ticket, request);
    }
    [Fact]
    public async Task Licensed_handoff_preserves_account_family_and_binds_only_its_Avalon_grant()
    {
        Grant(provider: "other-store"); var grant = Grant();
        var result = (await Handoff()).Reply;
        Assert.Equal(GameAuthStates.Authorized, result.State); Assert.Equal("avalon", result.LicenseSource);
        var context = (await Service().GetContextAsync(result.GameContextCredential!, true, default))!;
        Assert.Equal(7, context.AccountId); Assert.Equal(_family, context.LauncherFamilyId);
        Assert.Equal("avalon.base", context.ApplicationKey); Assert.Equal(grant.Id, context.LicenseId);
        Assert.Null(context.IdentityVerifiedAt); Assert.Null(context.IdentityValidUntil); Assert.Null(context.ProviderSubject);
        Assert.Equal(Now.AddMinutes(5), context.AuthorizationValidUntil);
        Assert.Empty(_identities.ReceivedCalls());
    }
    [Fact]
    public async Task Pending_handoff_keeps_identity_and_refresh_acquires_first_grant_without_ticket_replay()
    {
        var pending = await Handoff(); Assert.Equal(GameAuthStates.PendingLicense, pending.Reply.State);
        Assert.Equal("avalon", pending.Reply.LicenseSource);
        var before = (await Service().GetContextAsync(pending.Reply.GameContextCredential!, false, default))!;
        Assert.Equal(7, before.AccountId); Assert.Equal(_family, before.LauncherFamilyId); Assert.Null(before.LicenseId);
        var grant = Grant();
        var refreshed = await Service().RefreshAsync(pending.Reply.GameContextRefreshToken!, Guid.NewGuid(), default);
        var after = (await Service().GetContextAsync(refreshed.GameContextCredential!, true, default))!;
        Assert.Equal(before.Id, after.Id); Assert.Equal(grant.Id, after.LicenseId); Assert.Equal("avalon", refreshed.LicenseSource);
        Assert.Null(await _store.ReadAsync(RedisGameTicketStore.Key(pending.Ticket), default));
    }
    [Fact]
    public async Task Native_refresh_renews_bounded_authority_and_honors_earlier_grant_expiry()
    {
        var grant = Grant(Now.AddMinutes(6)); var initial = (await Handoff()).Reply;
        _clock.Advance(TimeSpan.FromMinutes(4));
        var renewal = await Service().RefreshAsync(initial.GameContextRefreshToken!, Guid.NewGuid(), default);
        Assert.Equal(GameAuthStates.Authorized, renewal.State); Assert.Equal(grant.ExpiresAt, renewal.AuthorizationValidUntil);
        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(await Service().GetContextAsync(renewal.GameContextCredential!, true, default));
        var expired = await Service().RefreshAsync(renewal.GameContextRefreshToken!, Guid.NewGuid(), default);
        Assert.Equal(GameAuthStates.PendingLicense, expired.State);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Revocation_including_future_timestamp_invalidates_handoff_and_refresh_receipts(bool future)
    {
        var grant = Grant(); var initial = await Handoff(); var request = Guid.NewGuid();
        var rotated = await Service().RefreshAsync(initial.Reply.GameContextRefreshToken!, request, default);
        Assert.Equal(GameAuthStates.Authorized, rotated.State);
        grant.RevokedAt = future ? Now.AddDays(1) : Now; grant.AuthorityRevision++;
        Grant(); // Replacement grant cannot revive an exact prior license binding.
        Assert.Null(await Service().GetContextAsync(rotated.GameContextCredential!, false, default));
        Assert.Equal(GameAuthErrors.ContextRevoked, (await Service().RefreshAsync(initial.Reply.GameContextRefreshToken!, request, default)).Error);
        Assert.Equal(GameAuthErrors.ContextRevoked, (await Service().RedeemHandoffAsync(initial.Attempt, initial.Ticket, initial.Request, default)).Error);
    }
    [Theory]
    [InlineData("account")]
    [InlineData("family")]
    [InlineData("epoch")]
    public async Task Account_and_launcher_family_changes_refuse_renewal(string changed)
    {
        Grant(); var initial = (await Handoff()).Reply;
        if (changed == "account") _account.Status = AccountStatus.Banned;
        if (changed == "epoch") _account.SessionEpoch++;
        if (changed == "family") _families.IsLiveLauncherFamilyAsync(_account.Id, _family, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(false);
        Assert.Equal(GameAuthErrors.ContextRevoked, (await Service().RefreshAsync(initial.GameContextRefreshToken!, Guid.NewGuid(), default)).Error);
    }
    [Fact]
    public async Task Database_failure_cannot_extend_existing_authority()
    {
        Grant(); var initial = (await Handoff()).Reply;
        var unavailable = Substitute.For<IGameLicenseRepository>();
        unavailable.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns<Task<GameLicense?>>(_ => throw new IOException("database down"));
        Assert.Equal(GameAuthErrors.ContextRevoked, (await Service(unavailable).RefreshAsync(initial.GameContextRefreshToken!, Guid.NewGuid(), default)).Error);
    }
    [Fact]
    public async Task Unknown_native_provider_is_not_an_attempt_authority()
    {
        var registry = new GameProviderRegistry([], []);
        var service = new GameAuthorizationService(_store, new(_store, _crypto, _options, _clock), _crypto, _accounts, _families, _identities,
            registry, new(registry, _licenses, _observations, _options), _options, _clock);
        Assert.Null(await service.CreateProviderAttemptAsync("avalon.base", "1", _run, new string('A', 43), null, 0, default));
    }
}
