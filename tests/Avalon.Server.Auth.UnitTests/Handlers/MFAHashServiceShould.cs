using Avalon.Common.ValueObjects;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.Auth.UnitTests.Handlers;

public class MFAHashServiceShould
{
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();
    private readonly MFAHashService _service;

    public MFAHashServiceShould()
    {
        _service = new MFAHashService(NullLoggerFactory.Instance, _cache);
    }

    [Fact]
    public async Task ReturnAccountId_ViaReverseLookup()
    {
        _cache.GetAsync(CacheKeys.MfaReverseHash("myhash")).Returns("42");

        var result = await _service.GetAccountIdAsync("myhash");

        Assert.NotNull(result);
        Assert.Equal(new AccountId(42L), result);
    }

    /// <summary>
    /// #495 review: two logins raced, one with the old password (version 0) and one with the new
    /// (version 1). The account's record now says 1, but hash H0 was issued by the old password's
    /// login. Its own reverse key says 0, and that is what counts.
    /// </summary>
    [Fact]
    public async Task Read_the_version_from_the_presented_hash_not_from_the_accounts_record()
    {
        _cache.GetAsync(CacheKeys.MfaReverseHash("H0")).Returns("42:0");
        _cache.GetAsync(CacheKeys.MfaReverseHash("H1")).Returns("42:1");
        StackExchange.Redis.IDatabase redis = Substitute.For<StackExchange.Redis.IDatabase>();
        _cache.Database.Returns(redis);
        redis.HashGetAsync((StackExchange.Redis.RedisKey)CacheKeys.AccountMfa(42), (StackExchange.Redis.RedisValue)"cver",
            Arg.Any<StackExchange.Redis.CommandFlags>()).Returns((StackExchange.Redis.RedisValue)"1");

        Assert.Equal(0, await _service.GetHashCredentialsVersionAsync("H0"));
        Assert.Equal(1, await _service.GetHashCredentialsVersionAsync("H1"));
        Assert.Equal(new AccountId(42L), await _service.GetAccountIdAsync("H0"));
    }

    [Theory]
    [InlineData("42")]
    [InlineData("42:")]
    [InlineData("42:x")]
    public async Task Give_no_version_for_a_hash_whose_reverse_key_carries_none(string value)
    {
        _cache.GetAsync(CacheKeys.MfaReverseHash("old")).Returns(value);

        Assert.Equal(-1, await _service.GetHashCredentialsVersionAsync("old"));
    }

    [Fact]
    public async Task ReturnNull_WhenHashNotFound()
    {
        _cache.GetAsync(CacheKeys.MfaReverseHash("missing")).Returns((string?)null);

        var result = await _service.GetAccountIdAsync("missing");

        Assert.Null(result);
    }

    /// <summary>#478: the DEL of the reverse key decides who spent the hash, as Redis tells one caller.</summary>
    [Fact]
    public async Task Spend_the_hash_only_for_the_caller_whose_delete_removed_it()
    {
        _cache.RemoveAsync(CacheKeys.MfaReverseHash("myhash")).Returns(true, false);

        bool first = await _service.TryConsumeAsync("myhash", new AccountId(42L));
        bool second = await _service.TryConsumeAsync("myhash", new AccountId(42L));

        Assert.True(first);
        Assert.False(second);
        await _cache.Received(1).RemoveAsync(CacheKeys.AccountMfa(42));
    }

    /// <summary>#478 re-review: the give-back decrements the attempts field, only on a live hash.</summary>
    [Fact]
    public async Task Give_an_attempt_back_on_the_accounts_live_hash()
    {
        await _service.GiveBackAttemptAsync(new AccountId(42L));

        await _cache.Received(1).HashDecrementFloorIfExistsAsync(CacheKeys.AccountMfa(42), "attempts");
    }

    [Fact]
    public async Task CleanupBothKeys_WhenHashExists()
    {
        _cache.GetAsync(CacheKeys.MfaReverseHash("myhash")).Returns("42");

        await _service.CleanupHash("myhash");

        await _cache.Received(1).RemoveAsync(CacheKeys.AccountMfa(42));
        await _cache.Received(1).RemoveAsync(CacheKeys.MfaReverseHash("myhash"));
    }

    [Fact]
    public async Task CleanupOnlyReverseKey_WhenHashNotFound()
    {
        _cache.GetAsync(CacheKeys.MfaReverseHash("gone")).Returns((string?)null);

        await _service.CleanupHash("gone");

        await _cache.DidNotReceive().RemoveAsync(Arg.Is<string>(k => k.Contains(":mfa") && !k.Contains("hash:")));
        await _cache.Received(1).RemoveAsync(CacheKeys.MfaReverseHash("gone"));
    }
}
