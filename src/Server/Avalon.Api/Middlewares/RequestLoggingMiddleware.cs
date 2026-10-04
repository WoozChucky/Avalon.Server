using System.Diagnostics;

namespace Avalon.Api.Middlewares;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<RequestLoggingMiddleware>();
        _next = next;
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
                Avalon.Api.Authentication.SteamOpenIdCallbackMiddleware.IsCallback(httpContext.Request.Path) ? QueryString.Empty : httpContext.Request.QueryString,
                httpContext.Response.StatusCode,
                stopwatch.Elapsed.TotalMilliseconds
            );
        }
    }
}
