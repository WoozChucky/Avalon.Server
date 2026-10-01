using System.Text.Json;
using Avalon.Api.Templates;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace Avalon.Api.UnitTests.Templates;

public class TemplateReloadSignalShould
{
    private const ushort World = 7;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();
    private readonly FakeTimeProvider _time = new();
    private readonly List<(string Channel, ReloadRequestMessage Request)> _published = [];
    private readonly List<Action<RedisChannel, RedisValue>> _handlers = [];
    private readonly List<string> _subscribed = [];

    public TemplateReloadSignalShould()
    {
        _cache.PublishAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(call =>
        {
            lock (_published)
            {
                _published.Add((call.ArgAt<string>(0),
                    JsonSerializer.Deserialize<ReloadRequestMessage>(call.ArgAt<string>(1), ReloadMessageJson.Options)!));
            }

            return Task.CompletedTask;
        });
        _cache.SubscribeAsync(Arg.Any<string>(), Arg.Any<Action<RedisChannel, RedisValue>>()).Returns(call =>
        {
            _subscribed.Add(call.ArgAt<string>(0));
            _handlers.Add(call.ArgAt<Action<RedisChannel, RedisValue>>(1));
            return Task.CompletedTask;
        });
    }

    private RedisTemplateReloadSignal Sut() => new(_cache,
        Options.Create(new TemplateEditingOptions { EditableWorlds = [World], ReloadTimeout = Timeout }),
        _time, NullLogger<RedisTemplateReloadSignal>.Instance);

    private static WorldId Id => new(World);

    private async Task<ReloadRequestMessage> NextPublishedAsync(int index)
    {
        for (int i = 0; i < 500; i++)
        {
            lock (_published)
            {
                if (_published.Count > index)
                {
                    return _published[index].Request;
                }
            }

            await Task.Delay(5);
        }

        throw new TimeoutException("No request was published.");
    }

    private void Answer(Guid requestId, params ReloadOutcomeMessage[] outcomes) =>
        _handlers[0](CacheKeys.WorldReloadResultChannel(World),
            ReloadMessageJson.Serialize(new ReloadResultMessage(requestId, outcomes)));

    [Fact]
    public async Task Publish_the_requested_area_on_the_worlds_reload_channel()
    {
        RedisTemplateReloadSignal sut = Sut();

        Task<TemplateReloadResult> pending = sut.RequestAsync(Id, TemplateReloadArea.Abilities, CancellationToken.None);
        ReloadRequestMessage request = await NextPublishedAsync(0);

        Assert.Equal(CacheKeys.WorldReloadChannel(World), _published[0].Channel);
        Assert.Equal(["Abilities"], request.Areas);
        Assert.NotEqual(Guid.Empty, request.RequestId);
        Assert.Equal([CacheKeys.WorldReloadResultChannel(World)], _subscribed);

        Answer(request.RequestId, new ReloadOutcomeMessage("Abilities", true, "12 abilities"));
        await pending;
    }

    [Fact]
    public async Task Not_miss_a_result_delivered_inside_the_publish_call()
    {
        // The world's answer can beat PublishAsync's own completion: the request must already be pending.
        _cache.PublishAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(call =>
        {
            var request = JsonSerializer.Deserialize<ReloadRequestMessage>(call.ArgAt<string>(1), ReloadMessageJson.Options)!;
            Answer(request.RequestId, new ReloadOutcomeMessage("Items", true, "instant"));
            return Task.CompletedTask;
        });
        RedisTemplateReloadSignal sut = Sut();

        TemplateReloadResult result = await sut.RequestAsync(Id, TemplateReloadArea.Items, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal((TemplateReloadResult.Applied, "instant"), (result.Status, result.Summary));
    }

    [Fact]
    public async Task Send_camel_case_json()
    {
        RedisTemplateReloadSignal sut = Sut();

        Task<TemplateReloadResult> pending = sut.RequestAsync(Id, TemplateReloadArea.Items, CancellationToken.None);
        await NextPublishedAsync(0);

        string raw = (string)_cache.ReceivedCalls().First(c => c.GetMethodInfo().Name == nameof(IReplicatedCache.PublishAsync))
            .GetArguments()[1]!;
        Assert.Contains("\"requestId\":", raw, StringComparison.Ordinal);
        Assert.Contains("\"areas\":[\"Items\"]", raw, StringComparison.Ordinal);

        _time.Advance(Timeout);
        await pending;
    }

    [Fact]
    public async Task Report_applied_when_every_outcome_succeeded()
    {
        RedisTemplateReloadSignal sut = Sut();
        Task<TemplateReloadResult> pending = sut.RequestAsync(Id, TemplateReloadArea.Items, CancellationToken.None);
        ReloadRequestMessage request = await NextPublishedAsync(0);

        Answer(request.RequestId, new ReloadOutcomeMessage("Items", true, "40 items"));

        TemplateReloadResult result = await pending;
        Assert.Equal(TemplateReloadResult.Applied, result.Status);
        Assert.Equal("40 items", result.Summary);
    }

    [Fact]
    public async Task Report_failed_with_the_failed_areas_summaries()
    {
        RedisTemplateReloadSignal sut = Sut();
        Task<TemplateReloadResult> pending = sut.RequestAsync(Id, TemplateReloadArea.Creatures, CancellationToken.None);
        ReloadRequestMessage request = await NextPublishedAsync(0);

        Answer(request.RequestId,
            new ReloadOutcomeMessage("Creatures", false, "InvalidOperationException. Nothing changed."),
            new ReloadOutcomeMessage("Items", true, "fine"));

        TemplateReloadResult result = await pending;
        Assert.Equal(TemplateReloadResult.Failed, result.Status);
        Assert.Equal("InvalidOperationException. Nothing changed.", result.Summary);
    }

    [Fact]
    public async Task Ignore_a_result_for_another_request_and_then_time_out_as_pending()
    {
        RedisTemplateReloadSignal sut = Sut();
        Task<TemplateReloadResult> pending = sut.RequestAsync(Id, TemplateReloadArea.Items, CancellationToken.None);
        await NextPublishedAsync(0);

        Answer(Guid.NewGuid(), new ReloadOutcomeMessage("Items", true, "not yours"));
        await Task.Delay(50);
        Assert.False(pending.IsCompleted);

        _time.Advance(Timeout);

        TemplateReloadResult result = await pending;
        Assert.Equal(TemplateReloadResult.Pending, result.Status);
        Assert.Null(result.Summary);
    }

    [Fact]
    public async Task Not_time_out_before_the_configured_wait()
    {
        RedisTemplateReloadSignal sut = Sut();
        Task<TemplateReloadResult> pending = sut.RequestAsync(Id, TemplateReloadArea.Items, CancellationToken.None);
        ReloadRequestMessage request = await NextPublishedAsync(0);

        _time.Advance(Timeout - TimeSpan.FromMilliseconds(1));
        await Task.Delay(50);
        Assert.False(pending.IsCompleted);

        Answer(request.RequestId, new ReloadOutcomeMessage("Items", true, "ok"));
        Assert.Equal(TemplateReloadResult.Applied, (await pending).Status);
    }

    [Fact]
    public async Task Answer_a_late_result_as_nothing_after_the_timeout()
    {
        RedisTemplateReloadSignal sut = Sut();
        Task<TemplateReloadResult> pending = sut.RequestAsync(Id, TemplateReloadArea.Items, CancellationToken.None);
        ReloadRequestMessage request = await NextPublishedAsync(0);
        _time.Advance(Timeout);
        Assert.Equal(TemplateReloadResult.Pending, (await pending).Status);

        // The entry is gone: a late answer is ignored and does not throw into the Redis callback.
        Answer(request.RequestId, new ReloadOutcomeMessage("Items", true, "late"));
    }

    [Fact]
    public async Task Return_pending_without_throwing_when_the_caller_cancels()
    {
        RedisTemplateReloadSignal sut = Sut();
        using var cts = new CancellationTokenSource();
        Task<TemplateReloadResult> pending = sut.RequestAsync(Id, TemplateReloadArea.Items, cts.Token);
        await NextPublishedAsync(0);

        await cts.CancelAsync();

        Assert.Equal(TemplateReloadResult.Pending, (await pending).Status);
    }

    [Fact]
    public async Task Give_two_concurrent_requests_their_own_results_even_when_answered_in_reverse()
    {
        RedisTemplateReloadSignal sut = Sut();
        Task<TemplateReloadResult> first = sut.RequestAsync(Id, TemplateReloadArea.Items, CancellationToken.None);
        ReloadRequestMessage firstRequest = await NextPublishedAsync(0);
        Task<TemplateReloadResult> second = sut.RequestAsync(Id, TemplateReloadArea.Abilities, CancellationToken.None);
        ReloadRequestMessage secondRequest = await NextPublishedAsync(1);
        Assert.NotEqual(firstRequest.RequestId, secondRequest.RequestId);

        Answer(secondRequest.RequestId, new ReloadOutcomeMessage("Abilities", false, "second failed"));
        Answer(firstRequest.RequestId, new ReloadOutcomeMessage("Items", true, "first fine"));

        TemplateReloadResult firstResult = await first;
        TemplateReloadResult secondResult = await second;
        Assert.Equal((TemplateReloadResult.Applied, "first fine"), (firstResult.Status, firstResult.Summary));
        Assert.Equal((TemplateReloadResult.Failed, "second failed"), (secondResult.Status, secondResult.Summary));
        Assert.Single(_subscribed);
    }
}
