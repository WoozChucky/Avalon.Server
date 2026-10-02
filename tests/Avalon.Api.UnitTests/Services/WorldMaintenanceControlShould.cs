using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.WorldMaintenance;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Services;

public sealed class WorldMaintenanceControlShould
{
    [Fact]
    public async Task Publish_committed_revision_and_preserve_state_when_delivery_fails()
    {
        var repository = Substitute.For<IWorldMaintenanceRepository>();
        var cache = Substitute.For<IReplicatedCache>();
        var now = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
        var committed = new WorldMaintenanceState(true, 3, now.AddMinutes(5));
        repository.TransitionAsync(new WorldId(1), true, TimeSpan.FromMinutes(5), now,
            Arg.Any<CancellationToken>()).Returns(committed);
        cache.PublishAsync(CacheKeys.WorldMaintenance(1), "3")
            .Returns(Task.FromException(new InvalidOperationException("Redis unavailable")));

        var control = new WorldMaintenanceControl(repository, cache,
            new FixedTimeProvider(now), NullLogger<WorldMaintenanceControl>.Instance);
        var result = await control.SetAsync(new WorldId(1), true, TimeSpan.FromMinutes(5), "admin:7",
            CancellationToken.None);

        Assert.Equal(committed, result);
        await cache.Received(1).PublishAsync(CacheKeys.WorldMaintenance(1), "3");
    }

    [Fact]
    public async Task Neither_log_nor_publish_a_request_that_changed_nothing()
    {
        var repository = Substitute.For<IWorldMaintenanceRepository>();
        var cache = Substitute.For<IReplicatedCache>();
        var now = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
        var standing = new WorldMaintenanceState(true, 3, now.AddMinutes(2));
        repository.ReadAsync(new WorldId(1), Arg.Any<CancellationToken>()).Returns(standing);
        repository.TransitionAsync(new WorldId(1), true, TimeSpan.FromMinutes(5), now,
            Arg.Any<CancellationToken>()).Returns(standing);
        var logger = new RecordingLogger();

        var control = new WorldMaintenanceControl(repository, cache, new FixedTimeProvider(now), logger);
        var result = await control.SetAsync(new WorldId(1), true, TimeSpan.FromMinutes(5), "admin:7",
            CancellationToken.None);

        Assert.Equal(standing, result);
        await cache.DidNotReceiveWithAnyArgs().PublishAsync(default!, default!);
        Assert.Empty(logger.Entries);
    }

    private sealed class RecordingLogger : Microsoft.Extensions.Logging.ILogger<WorldMaintenanceControl>
    {
        public List<string> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add(formatter(state, exception));
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
