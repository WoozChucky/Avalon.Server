using System.Text.Json;
using Avalon.Infrastructure;
using Avalon.World.Reload;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Reload;

public class ReloadRequestHandlerShould
{
    private const ushort World = 3;

    private readonly IReferenceDataReloader _reloader = Substitute.For<IReferenceDataReloader>();
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();
    private readonly CapturingLogger _logger = new();

    private ReloadRequestHandler Sut() => new(_reloader, _cache, World, _logger);

    private static string Request(Guid id, params string[] areas) =>
        ReloadMessageJson.Serialize(new ReloadRequestMessage(id, areas));

    private static ReloadOutcome Ok(ReloadArea area, string summary) =>
        new(area, true, summary, TimeSpan.FromMilliseconds(5), null);

    private ReloadResultMessage Published()
    {
        IEnumerable<object?[]> calls = _cache.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IReplicatedCache.PublishAsync))
            .Select(c => c.GetArguments());
        object?[] call = Assert.Single(calls);
        Assert.Equal(CacheKeys.WorldReloadResultChannel(World), call[0]);
        return JsonSerializer.Deserialize<ReloadResultMessage>((string)call[1]!, ReloadMessageJson.Options)!;
    }

    private void NothingPublished() =>
        Assert.DoesNotContain(_cache.ReceivedCalls(), c => c.GetMethodInfo().Name == nameof(IReplicatedCache.PublishAsync));

    [Fact]
    public async Task Reload_the_requested_areas_and_publish_the_outcomes_under_the_same_request_id()
    {
        var id = Guid.NewGuid();
        _reloader.ReloadAsync(Arg.Any<IReadOnlyList<ReloadArea>>(), Arg.Any<CancellationToken>())
            .Returns(new ReloadReport([Ok(ReloadArea.Items, "40 items"), Ok(ReloadArea.Creatures, "9 creatures")]));

        await Sut().HandleAsync(Request(id, "Items", "Creatures"));

        _ = _reloader.Received(1).ReloadAsync(
            Arg.Is<IReadOnlyList<ReloadArea>>(a => a.SequenceEqual(new[] { ReloadArea.Items, ReloadArea.Creatures })),
            Arg.Any<CancellationToken>());
        ReloadResultMessage result = Published();
        Assert.Equal(id, result.RequestId);
        Assert.Equal(
            [new ReloadOutcomeMessage("Items", true, "40 items"), new ReloadOutcomeMessage("Creatures", true, "9 creatures")],
            result.Outcomes);
    }

    [Fact]
    public async Task Report_a_failed_area_as_failed()
    {
        var id = Guid.NewGuid();
        _reloader.ReloadAsync(Arg.Any<IReadOnlyList<ReloadArea>>(), Arg.Any<CancellationToken>())
            .Returns(new ReloadReport([new ReloadOutcome(ReloadArea.Abilities, false, "", TimeSpan.Zero,
                new InvalidOperationException("secret detail"))]));

        await Sut().HandleAsync(Request(id, "Abilities"));

        ReloadOutcomeMessage outcome = Assert.Single(Published().Outcomes);
        Assert.Equal("Abilities", outcome.Area);
        Assert.False(outcome.Succeeded);
        Assert.Contains("InvalidOperationException", outcome.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("secret detail", outcome.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuse_an_unknown_area_as_a_failed_outcome_and_still_reload_the_known_ones()
    {
        var id = Guid.NewGuid();
        _reloader.ReloadAsync(Arg.Any<IReadOnlyList<ReloadArea>>(), Arg.Any<CancellationToken>())
            .Returns(new ReloadReport([Ok(ReloadArea.Items, "ok")]));

        await Sut().HandleAsync(Request(id, "Items", "Everything"));

        _ = _reloader.Received(1).ReloadAsync(
            Arg.Is<IReadOnlyList<ReloadArea>>(a => a.SequenceEqual(new[] { ReloadArea.Items })),
            Arg.Any<CancellationToken>());
        ReloadOutcomeMessage[] outcomes = Published().Outcomes;
        Assert.True(outcomes.Single(o => o.Area == "Items").Succeeded);
        Assert.False(outcomes.Single(o => o.Area == "Everything").Succeeded);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Reload_nothing_when_every_area_is_unknown()
    {
        await Sut().HandleAsync(Request(Guid.NewGuid(), "Maps"));

        _ = _reloader.DidNotReceive().ReloadAsync(Arg.Any<IReadOnlyList<ReloadArea>>(), Arg.Any<CancellationToken>());
        Assert.False(Assert.Single(Published().Outcomes).Succeeded);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"requestId\":\"00000000-0000-0000-0000-000000000000\",\"areas\":[\"Items\"]}")]
    [InlineData("{\"requestId\":\"2d6a1f4e-5a64-4a6b-9a2b-6f0c1e1f9b11\",\"areas\":[]}")]
    [InlineData("{\"requestId\":\"2d6a1f4e-5a64-4a6b-9a2b-6f0c1e1f9b11\",\"areas\":[null]}")]
    public async Task Log_a_malformed_message_and_publish_nothing(string message)
    {
        await Sut().HandleAsync(message);

        NothingPublished();
        _ = _reloader.DidNotReceive().ReloadAsync(Arg.Any<IReadOnlyList<ReloadArea>>(), Arg.Any<CancellationToken>());
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Answer_failed_and_not_throw_when_the_reloader_throws()
    {
        _reloader.ReloadAsync(Arg.Any<IReadOnlyList<ReloadArea>>(), Arg.Any<CancellationToken>())
            .Returns<Task<ReloadReport>>(_ => throw new InvalidOperationException("boom"));

        await Sut().HandleAsync(Request(Guid.NewGuid(), "Items"));

        Assert.False(Assert.Single(Published().Outcomes).Succeeded);
    }

    [Fact]
    public async Task Not_throw_when_publishing_fails()
    {
        _reloader.ReloadAsync(Arg.Any<IReadOnlyList<ReloadArea>>(), Arg.Any<CancellationToken>())
            .Returns(new ReloadReport([Ok(ReloadArea.Items, "ok")]));
        _cache.PublishAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns<Task>(_ => throw new InvalidOperationException("redis down"));

        await Sut().HandleAsync(Request(Guid.NewGuid(), "Items"));

        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error);
    }

    private sealed class CapturingLogger : ILogger<ReloadRequestHandler>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
            {
                Entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
