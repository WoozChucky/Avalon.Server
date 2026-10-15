using Avalon.Api.Hosting.Middlewares;
using Avalon.Api.Identity.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.GameAuth;

/// <summary>
/// A full world sends identity a lease heartbeat per player every 15 seconds, about 87 a second with 1,300 players
/// (#1051): a successful one is logged at Debug, which production does not write. Its failures, and every other
/// internal route, stay at Information.
/// </summary>
public sealed class WorkloadRequestLogShould
{
    [Theory]
    [InlineData("/internal/game/sessions/heartbeat", StatusCodes.Status200OK, false, LogLevel.Debug)]
    [InlineData("/INTERNAL/game/sessions/heartbeat", StatusCodes.Status200OK, false, LogLevel.Debug)]
    [InlineData("/internal/game/sessions/heartbeat", StatusCodes.Status401Unauthorized, false, LogLevel.Information)]
    [InlineData("/internal/game/sessions/heartbeat", StatusCodes.Status503ServiceUnavailable, false, LogLevel.Information)]
    // An exception that escaped before the exception handler set a status still reads 200 here: a failure.
    [InlineData("/internal/game/sessions/heartbeat", StatusCodes.Status200OK, true, LogLevel.Information)]
    [InlineData("/internal/game/sessions/end", StatusCodes.Status200OK, false, LogLevel.Information)]
    [InlineData("/internal/game/sessions/activate", StatusCodes.Status200OK, false, LogLevel.Information)]
    [InlineData("/internal/game/join-tickets/redeem", StatusCodes.Status200OK, false, LogLevel.Information)]
    [InlineData("/health", StatusCodes.Status200OK, false, LogLevel.None)]
    public void Log_a_successful_heartbeat_at_Debug_and_everything_else_as_before(string path, int status, bool failed, LogLevel level)
    {
        var services = new ServiceCollection();
        services.AddIdentityAuthentication();
        using ServiceProvider provider = services.BuildServiceProvider();
        RequestLoggingOptions options = provider.GetRequiredService<IOptions<RequestLoggingOptions>>().Value;

        Assert.Equal(level, RequestLoggingMiddleware.LevelOf(path, status, failed, options));
    }

    /// <summary>
    /// The middleware itself: a heartbeat an exception escaped from is not written off as a quiet 200. A caller that hung
    /// up is no failure: its request keeps the level it had, so a heartbeat stays Debug, a probe silent, and any other
    /// route at Information.
    /// </summary>
    [Theory]
    [InlineData("/internal/game/sessions/heartbeat", false, 1)]
    [InlineData("/internal/game/sessions/heartbeat", true, 0)]
    [InlineData("/health", true, 0)]
    [InlineData("/internal/game/sessions/end", true, 1)]
    public async Task Log_an_escaped_exception_as_a_failure_unless_the_caller_hung_up(string path, bool callerHungUp, int informationLines)
    {
        var options = new RequestLoggingOptions();
        options.QuietOnSuccess.Add(GameInternalRoutes.IsHeartbeat);
        var logs = new LevelCapture();
        using var aborted = new CancellationTokenSource();
        var middleware = new RequestLoggingMiddleware(_ => Task.FromException(callerHungUp
            ? new OperationCanceledException(aborted.Token)
            : new InvalidOperationException("fixture")), logs, Options.Create(options));
        var context = new DefaultHttpContext { RequestAborted = aborted.Token };
        context.Request.Method = "POST";
        context.Request.Path = path;
        if (callerHungUp) await aborted.CancelAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => middleware.InvokeAsync(context));

        Assert.Equal(informationLines, logs.Levels.Count(level => level == LogLevel.Information));
        Assert.Equal(informationLines, logs.Levels.Count);
    }

    private sealed class LevelCapture : ILoggerFactory, ILogger
    {
        public List<LogLevel> Levels { get; } = [];
        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
    }
}
