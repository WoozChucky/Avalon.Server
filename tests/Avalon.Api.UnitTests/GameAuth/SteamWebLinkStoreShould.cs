using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.GameAuth;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Avalon.Api.UnitTests.GameAuth;

public sealed class SteamWebLinkStoreShould
{
    private readonly AtomicAuthStore _memory = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
    private readonly Account _root = new() { Id = new AccountId(7), Username = "PLAYER", Email = "player@example.test", JoinDate = DateTime.UnixEpoch, Salt = [1], Verifier = [2] };
    private SteamWebLinkStore Store() => new(_memory, new GameAuthCryptography(new byte[32]), _clock);
    private string Nonce(string tail = "unique") => "2026-10-04T12:00:00Z" + tail;

    [Fact]
    public async Task Bind_start_retries_to_root_browser_session_and_secret_cookie()
    {
        var id = Guid.NewGuid();
        var first = await Store().StartAsync(id, _root, "browser-a", default);
        Assert.NotNull(first);
        Assert.Equal(first, await Store().StartAsync(id, _root, "browser-a", default));
        Assert.Null(await Store().StartAsync(id, _root, "browser-b", default));
        Assert.Null(await Store().ReadBoundAsync(id, _root.Id, "browser-a", GameAuthCryptography.NewToken(), default));
        Assert.DoesNotContain(first!.Cookie, string.Join("", _memory.Entries.Values));
    }

    [Fact]
    public async Task Consume_challenge_and_provider_nonce_once_even_across_transactions()
    {
        var a = await Store().StartAsync(Guid.NewGuid(), _root, "browser-a", default);
        var b = await Store().StartAsync(Guid.NewGuid(), _root, "browser-a", default);
        Assert.True(await Store().ChallengeAsync(a!.Id, a.Cookie, default));
        Assert.False(await Store().ChallengeAsync(a.Id, a.Cookie, default));
        Assert.True(await Store().ChallengeAsync(b!.Id, b.Cookie, default));
        Assert.True(await Store().VerifyAsync(a.Id, a.Cookie, _root, "https://steamcommunity.com/openid/id/76561198000000001", Nonce(), default));
        Assert.False(await Store().VerifyAsync(b.Id, b.Cookie, _root, "https://steamcommunity.com/openid/id/76561198000000001", Nonce(), default));
        Assert.DoesNotContain(Nonce(), string.Join("", _memory.Entries.Values));
    }

    [Fact]
    public async Task Reject_changed_authority_stale_nonce_malformed_identity_and_expired_proof()
    {
        var a = await Store().StartAsync(Guid.NewGuid(), _root, "browser-a", default);
        Assert.True(await Store().ChallengeAsync(a!.Id, a.Cookie, default));
        _root.SessionEpoch++;
        Assert.False(await Store().VerifyAsync(a.Id, a.Cookie, _root, "https://steamcommunity.com/openid/id/76561198000000001", Nonce(), default));
        _root.SessionEpoch--;
        Assert.False(await Store().VerifyAsync(a.Id, a.Cookie, _root, "https://steamcommunity.com/openid/id/76561198000000001?extra=1", Nonce(), default));
        Assert.False(await Store().VerifyAsync(a.Id, a.Cookie, _root, "https://steamcommunity.com/openid/id/76561198000000001", "2026-10-04T11:57:59Zstale", default));
        Assert.True(await Store().VerifyAsync(a.Id, a.Cookie, _root, "https://steamcommunity.com/openid/id/76561198000000001", Nonce(), default));
        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(await Store().CommitAsync(a.Id, _root, "browser-a", a.Cookie, Guid.NewGuid(), null, default));
    }

    [Fact]
    public async Task Keep_exact_authorized_commit_retry_after_epoch_changes_without_accepting_new_consent()
    {
        var a = await Store().StartAsync(Guid.NewGuid(), _root, "browser-a", default);
        await Store().ChallengeAsync(a!.Id, a.Cookie, default);
        await Store().VerifyAsync(a.Id, a.Cookie, _root, "https://steamcommunity.com/openid/id/76561198000000001", Nonce(), default);
        var confirm = Guid.NewGuid();
        var races = await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Store().CommitAsync(a.Id, _root, "browser-a", a.Cookie, confirm, null, default)));
        Assert.All(races, r => Assert.NotNull(r));
        _root.SessionEpoch++;
        _clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(races[0], await Store().CommitAsync(a.Id, _root, "browser-a", a.Cookie, confirm, null, default));
        Assert.Null(await Store().CommitAsync(a.Id, _root, "browser-a", a.Cookie, Guid.NewGuid(), null, default));
        Assert.Null(await Store().CommitAsync(a.Id, _root, "browser-a", a.Cookie, confirm, null, default, true));
    }
}
