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

    [Fact]
    public async Task ReturnNull_WhenHashNotFound()
    {
        _cache.GetAsync(CacheKeys.MfaReverseHash("missing")).Returns((string?)null);

        var result = await _service.GetAccountIdAsync("missing");

        Assert.Null(result);
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
