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

    /// <summary>The middleware itself: a heartbeat an exception escaped from is not written off as a quiet 200.</summary>
    [Fact]
    public async Task Log_a_heartbeat_an_exception_escaped_from_at_Information()
    {
        var options = new RequestLoggingOptions();
        options.QuietOnSuccess.Add(GameInternalRoutes.IsHeartbeat);
        var logs = new LevelCapture();
        var middleware = new RequestLoggingMiddleware(_ => throw new InvalidOperationException("fixture"), logs, Options.Create(options));
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = GameInternalRoutes.HeartbeatPath;

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        Assert.Equal(new[] { LogLevel.Information }, logs.Levels);
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
