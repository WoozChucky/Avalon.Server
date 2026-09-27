using System.Collections.Concurrent;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Services;

/// <summary>
/// Launcher sign-in codes (#591): what the signed-in website hands to a launcher's loopback port, and
/// the launcher trades, with its PKCE verifier, for its own session. One use, one minute.
/// </summary>
public class LauncherAuthCodesShould
{
    // RFC 7636, appendix B.
    private const string Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
    private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

    private readonly ConcurrentDictionary<string, string> _store = new();
    private readonly List<(string Key, TimeSpan? Ttl)> _writes = [];
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();

    public LauncherAuthCodesShould()
    {
        _cache.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>()).Returns(ci =>
        {
            _store[ci.ArgAt<string>(0)] = ci.ArgAt<string>(1);
            lock (_writes) _writes.Add((ci.ArgAt<string>(0), ci.ArgAt<TimeSpan?>(2)));
            return true;
        });
        _cache.GetAsync(Arg.Any<string>()).Returns(ci => _store.TryGetValue(ci.Arg<string>(), out string? v) ? v : null);
        // Atomic, like Redis DEL: only one caller learns it removed the key.
        _cache.RemoveAsync(Arg.Any<string>()).Returns(ci => _store.TryRemove(ci.Arg<string>(), out _));
    }

    private LauncherAuthCodes Codes() => new(_cache, new SecureRandom());

    [Fact]
    public void Compute_the_rfc_7636_example_challenge() => Assert.Equal(Challenge, Pkce.ChallengeOf(Verifier));

    [Fact]
    public async Task Hand_back_the_grant_to_the_verifier_that_matches()
    {
        LauncherAuthCodes codes = Codes();
        string code = await codes.IssueAsync(new AccountId(7L), 3, Challenge, 49152);

        LauncherGrant? grant = await codes.RedeemAsync(code, Verifier);

        Assert.Equal(new LauncherGrant(new AccountId(7L), 3, 49152), grant);
    }

    [Fact]
    public async Task Spend_the_code_even_when_the_verifier_is_wrong()
    {
        LauncherAuthCodes codes = Codes();
        string code = await codes.IssueAsync(new AccountId(7L), 3, Challenge, 49152);

        Assert.Null(await codes.RedeemAsync(code, new string('a', 43)));
        Assert.Null(await codes.RedeemAsync(code, Verifier)); // one guess per code
    }

    [Fact]
    public async Task Redeem_a_code_once_when_two_exchanges_race()
    {
        LauncherAuthCodes codes = Codes();
        string code = await codes.IssueAsync(new AccountId(7L), 3, Challenge, 49152);

        LauncherGrant?[] results = await Task.WhenAll(codes.RedeemAsync(code, Verifier), codes.RedeemAsync(code, Verifier));

        Assert.Single(results, r => r is not null);
    }

    [Fact]
    public async Task Store_the_code_by_its_hash_for_sixty_seconds()
    {
        string code = await Codes().IssueAsync(new AccountId(7L), 3, Challenge, 49152);

        (string key, TimeSpan? ttl) = Assert.Single(_writes);
        Assert.DoesNotContain(code, key);
        Assert.StartsWith("auth:launcherCode:", key);
        Assert.Equal(TimeSpan.FromSeconds(60), ttl);
    }

    [Fact]
    public async Task Refuse_a_verifier_that_is_not_one()
    {
        LauncherAuthCodes codes = Codes();
        string code = await codes.IssueAsync(new AccountId(7L), 3, Challenge, 49152);

        Assert.Null(await codes.RedeemAsync(code, "short"));
    }

    [Theory]
    [InlineData("short", 49152)]
    [InlineData("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-c+", 49152)]
    [InlineData(Challenge, 80)]
    [InlineData(Challenge, 70000)]
    public async Task Refuse_a_challenge_or_port_it_cannot_honour(string challenge, int port) =>
        await Assert.ThrowsAsync<ArgumentException>(() => Codes().IssueAsync(new AccountId(7L), 3, challenge, port));

    [Fact]
    public async Task Spend_the_code_even_when_the_verifier_is_malformed()
    {
        // "Spent by its first redemption, right or wrong" (#591 review): a malformed verifier too.
        LauncherAuthCodes codes = Codes();
        string code = await codes.IssueAsync(new AccountId(7L), 3, Challenge, 49152);

        Assert.Null(await codes.RedeemAsync(code, "short"));
        Assert.Null(await codes.RedeemAsync(code, Verifier));
    }
}
