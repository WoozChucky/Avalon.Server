using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Avalon.Api.Distribution.UnitTests;

/// <summary>
/// #1252: the anonymous upload-complete notice passes on only a key shaped like a slot this API issued today or
/// yesterday, and answers alike whether or not the worker is reached, so it says nothing about what exists.
/// </summary>
public class CrashNoticeServiceShould
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 29, 0, 30, 0, TimeSpan.Zero);
    private const string Id = "0123456789abcdef0123456789abcdef";
    private readonly ICrashNoticeForwarder _forwarder = Substitute.For<ICrashNoticeForwarder>();

    [Theory]
    [InlineData("0.4.1-dev.10/2026-10-29/" + Id + ".zip")]
    [InlineData("0.4.1+7.abc/2026-10-28/" + Id + ".zip")]
    public async Task Pass_on_a_key_issued_today_or_yesterday(string key)
    {
        var service = new CrashNoticeService(_forwarder, new FakeTimeProvider(s_now));

        Assert.True(await service.NoticeAsync(key, CancellationToken.None));
        await _forwarder.Received(1).ForwardAsync(key, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0.4.1/2026-10-27/" + Id + ".zip")]
    [InlineData("0.4.1/2026-10-30/" + Id + ".zip")]
    [InlineData("0.4.1/2026-10-29/" + Id + ".dmp")]
    [InlineData("0.4.1/2026-10-29/" + Id + "0.zip")]
    [InlineData("0.4.1/2026-10-29/xyz456789abcdef0123456789abcdef.zip")]
    [InlineData("../2026-10-29/" + Id + ".zip")]
    [InlineData("0.4.1/2026-10-29/extra/" + Id + ".zip")]
    public async Task Refuse_a_key_no_slot_could_have_today_without_passing_it_on(string? key)
    {
        var service = new CrashNoticeService(_forwarder, new FakeTimeProvider(s_now));

        Assert.False(await service.NoticeAsync(key, CancellationToken.None));
        await _forwarder.DidNotReceiveWithAnyArgs().ForwardAsync(default!, default);
    }

    [Fact]
    public void Refuse_a_notice_url_set_wrong_at_startup_and_hold_its_timeout_in_bounds()
    {
        Assert.Null(CrashNoticeOptions.From(new DistributionConfiguration()));
        Assert.Throws<InvalidOperationException>(() => CrashNoticeOptions.From(new DistributionConfiguration { CrashNoticeUrl = "worker:8080" }));
        CrashNoticeOptions options = CrashNoticeOptions.From(new DistributionConfiguration
        {
            CrashNoticeUrl = "http://worker:8080/internal/crash/notice",
            CrashNoticeTimeoutMs = -5,
        })!;
        Assert.Equal(TimeSpan.FromMilliseconds(100), options.Timeout);
    }

    [Fact]
    public async Task Answer_a_well_formed_key_alike_when_the_worker_cannot_be_reached()
    {
        _forwarder.ForwardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException());
        var service = new CrashNoticeService(_forwarder, new FakeTimeProvider(s_now));

        Assert.True(await service.NoticeAsync("0.4.1/2026-10-29/" + Id + ".zip", CancellationToken.None));
    }
}
