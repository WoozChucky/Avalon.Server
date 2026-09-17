using System.Net;
using System.Security.Authentication;
using Avalon.Api.Exceptions;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace Avalon.Api.Middlewares;

public class ExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlerMiddleware> _logger;

    public ExceptionHandlerMiddleware(RequestDelegate next, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ExceptionHandlerMiddleware>();
        _next = next;
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        try
        {
            await _next(httpContext);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(httpContext, ex);
        }
    }
    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        switch (exception)
        {
            case AuthenticationException ex:
                context.Request.HttpContext.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = (int)HttpStatusCode.Unauthorized,
                    Type = exception.GetType().Name,
                    Title = "Whoops!",
                    Detail = ex.Message,
                    Instance = $"{context.Request.Method} {context.Request.Path}"
                }, cancellationToken: context.RequestAborted);
                return;
            case BusinessException ex:
                context.Request.HttpContext.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = (int)HttpStatusCode.BadRequest,
                    Type = exception.GetType().Name,
                    Title = "Client error",
                    Detail = ex.Message,
                    Instance = $"{context.Request.Method} {context.Request.Path}"
                }, cancellationToken: context.RequestAborted);
                return;
            // Redis unreachable must read as "service unavailable", not "server error": an
            // empty roster and a broken pipe must not look alike to a caller. This is a
            // shared middleware, so the mapping applies everywhere IReplicatedCache is used
            // (observability, account/refresh, MFA), not just the presence endpoints that
            // motivated it -- a Redis outage genuinely is a 503 everywhere.
            case RedisConnectionException ex:
                context.Request.HttpContext.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = (int)HttpStatusCode.ServiceUnavailable,
                    Type = exception.GetType().Name,
                    Title = "Service unavailable",
                    Detail = ex.Message,
                    Instance = $"{context.Request.Method} {context.Request.Path}"
                }, cancellationToken: context.RequestAborted);
                return;
        }

        _logger.LogError(exception, "An unexpected error occurred");

        context.Request.HttpContext.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = (int)HttpStatusCode.InternalServerError,
            Type = "ServerError",
            Title = "An unexpected error occurred",
            Detail = "An unexpected error occurred",
            Instance = $"{context.Request.Method} {context.Request.Path}"
        }, cancellationToken: context.RequestAborted);
    }
}
