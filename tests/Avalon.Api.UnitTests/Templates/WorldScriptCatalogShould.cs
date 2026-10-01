using Avalon.Api.Templates;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Microsoft.Extensions.Logging;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace Avalon.Api.UnitTests.Templates;

/// <summary>
/// A catalog Redis cannot give is "not published", the same no-check answer as an absent key: before the catalog,
/// a template save worked without Redis, and it still must.
/// </summary>
public sealed class WorldScriptCatalogShould
{
    private const ushort World = 7;

    // Redis errors carry the key and sometimes the value; the log line must carry neither.
    private const string Payload = "payload-that-must-not-be-logged";

    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();
    private readonly RecordingLogger _log = new();

    public static TheoryData<Exception> Failures => new()
    {
#pragma warning disable CS0618 // the overload the other Redis failure tests use
        new RedisConnectionException(ConnectionFailureType.UnableToConnect, Payload),
#pragma warning restore CS0618
        new RedisTimeoutException(Payload, CommandStatus.Unknown),
        new RedisServerException("WRONGTYPE " + Payload),
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Count_a_cache_that_throws_as_not_published(Exception failure)
    {
        _cache.GetAsync(Arg.Any<string>()).Returns(Task.FromException<string?>(failure));

        var catalog = new WorldScriptCatalog(_cache, _log);

        Assert.Null(await catalog.GetAsync(new WorldId(World), CancellationToken.None));
    }

    [Fact]
    public async Task Log_a_warning_naming_the_world_and_never_the_error_text()
    {
        _cache.GetAsync(Arg.Any<string>())
            .Returns(Task.FromException<string?>(new RedisServerException("WRONGTYPE " + Payload)));

        await new WorldScriptCatalog(_cache, _log).GetAsync(new WorldId(World), CancellationToken.None);

        (LogLevel level, string message) = Assert.Single(_log.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains($"{World}", message);
        Assert.DoesNotContain(Payload, message);
    }

    [Fact]
    public async Task Not_swallow_a_cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new WorldScriptCatalog(_cache, _log).GetAsync(new WorldId(World), cts.Token));
    }

    private sealed class RecordingLogger : ILogger<WorldScriptCatalog>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
