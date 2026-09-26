using System.Data.Common;
using System.Net;
using System.Security.Authentication;
using Avalon.Api.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Storage;
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
    // Fixed wording only: the exception's type names the driver and its message can carry hosts
    // and ports. Both stay in the log (#480).
    private static Task WriteServiceUnavailableAsync(HttpContext context) =>
        context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = (int)HttpStatusCode.ServiceUnavailable,
            Type = "ServiceUnavailable",
            Title = "Service unavailable",
            Detail = "The service is temporarily unavailable. Try again shortly.",
            Instance = $"{context.Request.Method} {context.Request.Path}"
        }, cancellationToken: context.RequestAborted);

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
            // Only thrown once the caller has proved they hold the account (password or MFA code).
            case AccountInactiveException ex:
                context.Request.HttpContext.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = (int)HttpStatusCode.Forbidden,
                    Type = exception.GetType().Name,
                    Title = "Account not active",
                    Detail = ex.Message,
                    Instance = $"{context.Request.Method} {context.Request.Path}"
                }, cancellationToken: context.RequestAborted);
                return;
            // A spent budget or a locked account (#478): the same answer for every username.
            case AccountLockedException ex:
                context.Request.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Type = exception.GetType().Name,
                    Title = "Too many attempts",
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
            // The same holds for the database. Every authenticated request reloads its account
            // (#480), so a database outage would otherwise surface as a 500 on every call.
            // Authentication fails closed either way: the request never reaches the endpoint.
            case DbException or RetryLimitExceededException:
                context.Request.HttpContext.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                _logger.LogError(exception, "Database unavailable");
                await WriteServiceUnavailableAsync(context);
                return;
            case RedisConnectionException:
                context.Request.HttpContext.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                _logger.LogError(exception, "Cache unavailable");
                await WriteServiceUnavailableAsync(context);
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
