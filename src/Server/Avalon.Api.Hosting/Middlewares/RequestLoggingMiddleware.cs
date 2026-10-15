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
        bool failed = false;

        try
        {
            // Call the next middleware in the pipeline
            await _next(httpContext);
        }
        catch
        {
            // The exception handler runs outside this middleware, so the status code still reads as the default 200
            // here (#1051): an exception that escaped is a failure whatever it says.
            failed = true;
            throw;
        }
        finally
        {
            stopwatch.Stop();

            // A healthy probe answer is not logged: Kubernetes asks /health and /alive every few seconds. One that
            // fails is, like any other request. A success on a path a service names as routine (#1051) is Debug.
            LogLevel level = LevelOf(httpContext.Request.Path, httpContext.Response.StatusCode, failed, _options);
            if (level != LogLevel.None && _logger.IsEnabled(level))
            {
                if (failed)
                {
                    _logger.Log(level, "HTTP {Method} {Path}{Query} failed with an exception in {Elapsed:0.0000} ms",
                        httpContext.Request.Method,
                        httpContext.Request.Path,
                        _options.HidesQueryOf(httpContext.Request.Path) ? QueryString.Empty : httpContext.Request.QueryString,
                        stopwatch.Elapsed.TotalMilliseconds);
                }
                else
                {
                    _logger.Log(level, "HTTP {Method} {Path}{Query} responded {StatusCode} in {Elapsed:0.0000} ms",
                        httpContext.Request.Method,
                        httpContext.Request.Path,
                        _options.HidesQueryOf(httpContext.Request.Path) ? QueryString.Empty : httpContext.Request.QueryString,
                        httpContext.Response.StatusCode,
                        stopwatch.Elapsed.TotalMilliseconds);
                }
            }
        }
    }

    /// <summary>
    /// The level of a request's line: none for a healthy probe, Debug for a success (2xx) on a path
    /// <see cref="RequestLoggingOptions.QuietOnSuccess"/> names, Information for everything else. A request through
    /// which an exception escaped (<paramref name="failed"/>) is never a success, whatever its status code reads.
    /// </summary>
    public static LogLevel LevelOf(PathString path, int statusCode, bool failed, RequestLoggingOptions options)
    {
        if (failed) return LogLevel.Information;
        if (IsProbe(path) && statusCode == StatusCodes.Status200OK) return LogLevel.None;
        return statusCode is >= 200 and < 300 && options.IsQuietOnSuccess(path) ? LogLevel.Debug : LogLevel.Information;
    }

    /// <summary>The health probes' paths, <c>/health</c> and <c>/alive</c> (<c>MapDefaultEndpoints</c>).</summary>
    public static bool IsProbe(PathString path) =>
        path.Equals("/health", StringComparison.OrdinalIgnoreCase) || path.Equals("/alive", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// What <see cref="RequestLoggingMiddleware"/> leaves out of its line (#794): the query string of the paths a service
/// names, such as a callback whose query carries a provider's assertion; and which paths' successes it writes at Debug
/// (#1051), such as a game server's lease heartbeat, a few dozen a second with a full world.
/// </summary>
public sealed class RequestLoggingOptions
{
    /// <summary>Tests for the paths whose query string is not logged.</summary>
    public IList<Func<PathString, bool>> HideQueryString { get; } = [];

    /// <summary>
    /// Tests for the paths whose successful (2xx) requests are logged at Debug, not Information: routine calls whose
    /// count and latency the <c>http.server.request.duration</c> metric already carries. A failure is Information.
    /// </summary>
    public IList<Func<PathString, bool>> QuietOnSuccess { get; } = [];

    /// <summary>Whether the query string of a request to <paramref name="path"/> is left out of the log.</summary>
    public bool HidesQueryOf(PathString path) => HideQueryString.Any(hides => hides(path));

    /// <summary>Whether a successful request to <paramref name="path"/> is logged at Debug.</summary>
    public bool IsQuietOnSuccess(PathString path)
    {
        foreach (Func<PathString, bool> quiet in QuietOnSuccess)
        {
            if (quiet(path)) return true;
        }

        return false;
    }
}
