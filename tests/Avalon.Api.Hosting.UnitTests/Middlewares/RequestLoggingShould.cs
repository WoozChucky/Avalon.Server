using Avalon.Api.Hosting.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Avalon.Api.Hosting.UnitTests.Middlewares;

/// <summary>
/// Every request is logged with its method, path and query, except the query of a path a service hides (#794): a
/// callback whose query carries a provider's assertion.
/// </summary>
public sealed class RequestLoggingShould
{
    [Fact]
    public async Task Log_the_path_and_the_query()
    {
        string line = await LogAsync("/account", "?page=2", new RequestLoggingOptions());

        Assert.StartsWith("HTTP GET /account?page=2 responded 204 in ", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Leave_out_the_query_of_a_path_a_service_hides()
    {
        var options = new RequestLoggingOptions();
        options.HideQueryString.Add(path => path.StartsWithSegments("/callback", StringComparison.OrdinalIgnoreCase));

        string hidden = await LogAsync("/callback", "?assertion=signed", options);
        string shown = await LogAsync("/other", "?page=2", options);

        Assert.StartsWith("HTTP GET /callback responded 204 in ", hidden, StringComparison.Ordinal);
        Assert.DoesNotContain("assertion", hidden, StringComparison.Ordinal);
        Assert.StartsWith("HTTP GET /other?page=2 responded 204 in ", shown, StringComparison.Ordinal);
    }

    private static async Task<string> LogAsync(string path, string query, RequestLoggingOptions options)
    {
        var logs = new CapturingLoggerFactory();
        var middleware = new RequestLoggingMiddleware(context =>
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        }, logs, Options.Create(options));
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);

        await middleware.InvokeAsync(context);

        return Assert.Single(logs.Lines);
    }

    private sealed class CapturingLoggerFactory : ILoggerFactory, ILogger
    {
        public List<string> Lines { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public void AddProvider(ILoggerProvider provider) { }

        public void Dispose() { }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
    }
}
