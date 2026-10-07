using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Hosting.Middlewares;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;
    private readonly RequestLoggingOptions _options;

    public RequestLoggingMiddleware(RequestDelegate next, ILoggerFactory loggerFactory,
        IOptions<RequestLoggingOptions> options)
    {
        _logger = loggerFactory.CreateLogger<RequestLoggingMiddleware>();
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        try
        {
            // Call the next middleware in the pipeline
            await _next(httpContext);
        }
        finally
        {
            stopwatch.Stop();

            _logger.LogInformation("HTTP {Method} {Path}{Query} responded {StatusCode} in {Elapsed:0.0000} ms",
                httpContext.Request.Method,
                httpContext.Request.Path,
                _options.HidesQueryOf(httpContext.Request.Path) ? QueryString.Empty : httpContext.Request.QueryString,
                httpContext.Response.StatusCode,
                stopwatch.Elapsed.TotalMilliseconds
            );
        }
    }
}

/// <summary>
/// What <see cref="RequestLoggingMiddleware"/> leaves out of its line (#794): the query string of the paths a service
/// names, such as a callback whose query carries a provider's assertion.
/// </summary>
public sealed class RequestLoggingOptions
{
    /// <summary>Tests for the paths whose query string is not logged.</summary>
    public IList<Func<PathString, bool>> HideQueryString { get; } = [];

    /// <summary>Whether the query string of a request to <paramref name="path"/> is left out of the log.</summary>
    public bool HidesQueryOf(PathString path) => HideQueryString.Any(hides => hides(path));
}
