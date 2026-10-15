using Avalon.Api.Hosting.Middlewares;
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
    [InlineData("/internal/game/sessions/heartbeat", StatusCodes.Status200OK, LogLevel.Debug)]
    [InlineData("/INTERNAL/game/sessions/heartbeat", StatusCodes.Status200OK, LogLevel.Debug)]
    [InlineData("/internal/game/sessions/heartbeat", StatusCodes.Status401Unauthorized, LogLevel.Information)]
    [InlineData("/internal/game/sessions/heartbeat", StatusCodes.Status503ServiceUnavailable, LogLevel.Information)]
    [InlineData("/internal/game/sessions/end", StatusCodes.Status200OK, LogLevel.Information)]
    [InlineData("/internal/game/sessions/activate", StatusCodes.Status200OK, LogLevel.Information)]
    [InlineData("/internal/game/join-tickets/redeem", StatusCodes.Status200OK, LogLevel.Information)]
    [InlineData("/health", StatusCodes.Status200OK, LogLevel.None)]
    public void Log_a_successful_heartbeat_at_Debug_and_everything_else_as_before(string path, int status, LogLevel level)
    {
        var services = new ServiceCollection();
        services.AddIdentityAuthentication();
        using ServiceProvider provider = services.BuildServiceProvider();
        RequestLoggingOptions options = provider.GetRequiredService<IOptions<RequestLoggingOptions>>().Value;

        Assert.Equal(level, RequestLoggingMiddleware.LevelOf(path, status, options));
    }
}
