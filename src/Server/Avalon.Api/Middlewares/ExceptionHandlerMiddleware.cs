using System.Data.Common;
using System.Net;
using System.Security.Authentication;
using Avalon.Api.Exceptions;
using Avalon.Database.Auth;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
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

    public const string AccountValueRefused = "The value is not in a form an account can store.";

    /// <summary>
    /// A Postgres check violation (SQLSTATE 23514) of one of the two constraints that hold an
    /// account's username and email in their stored form, raised directly by a bulk update or
    /// wrapped in a <see cref="DbUpdateException"/> by SaveChanges. Any other check violation is not
    /// the caller's value and keeps its usual mapping.
    /// </summary>
    private static bool IsAccountsCheckViolation(Exception exception) =>
        (exception as PostgresException ?? (exception as DbUpdateException)?.InnerException as PostgresException) is
        {
            SqlState: PostgresErrorCodes.CheckViolation, TableName: "Accounts",
            ConstraintName: AuthDbContext.UsernameNormalisedConstraint or AuthDbContext.EmailNormalisedConstraint,
        };

    // The cases are matched in this order, so a subtype takes the first case its type fits.
    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        await (exception switch
        {
            // A refresh that lost a race (#543). The refresh endpoint answers it itself; should it
            // ever get this far it is still that endpoint's 401, not a 500.
            RefreshAlreadyRotatedException => WriteRefreshAlreadyRotatedAsync(context),
            AuthenticationException => WriteProblemAsync(context, (int)HttpStatusCode.Unauthorized,
                exception.GetType().Name, "Whoops!", exception.Message),
            // Only thrown once the caller has proved they hold the account (password or MFA code).
            AccountInactiveException => WriteProblemAsync(context, (int)HttpStatusCode.Forbidden,
                exception.GetType().Name, "Account not active", exception.Message),
            // A spent budget or a locked account (#478): the same answer for every username.
            AccountLockedException => WriteProblemAsync(context, StatusCodes.Status429TooManyRequests,
                exception.GetType().Name, "Too many attempts", exception.Message),
            // An email the request needed could not be sent (#510). The thrower logged it, by type
            // and domain only; this exception carries nothing more.
            EmailDeliveryException => WriteProblemAsync(context, (int)HttpStatusCode.ServiceUnavailable,
                "ServiceUnavailable", "Service unavailable", exception.Message),
            // Game distribution: no store configured, or a published build that is not there.
            Distribution.DistributionUnavailableException => WriteProblemAsync(context, (int)HttpStatusCode.ServiceUnavailable,
                "ServiceUnavailable", "Service unavailable", exception.Message),
            BusinessException => WriteProblemAsync(context, (int)HttpStatusCode.BadRequest,
                exception.GetType().Name, "Client error", exception.Message),
            // An Accounts check constraint refused the row (#503 follow-up): a username or an email
            // that is not in its stored form. The caller's value, not an outage. The constraint's
            // name stays in the log.
            _ when IsAccountsCheckViolation(exception) => WriteAccountValueRefusedAsync(context, exception),
            // Redis unreachable must read as "service unavailable", not "server error": an
            // empty roster and a broken pipe must not look alike to a caller. This is a
            // shared middleware, so the mapping applies everywhere IReplicatedCache is used
            // (observability, account/refresh, MFA), not just the presence endpoints that
            // motivated it -- a Redis outage genuinely is a 503 everywhere.
            // The same holds for the database. Every authenticated request reloads its account
            // (#480), so a database outage would otherwise surface as a 500 on every call.
            // Authentication fails closed either way: the request never reaches the endpoint.
            DbException or RetryLimitExceededException => WriteDatabaseUnavailableAsync(context, exception),
            RedisConnectionException => WriteCacheUnavailableAsync(context, exception),
            _ => WriteUnexpectedErrorAsync(context, exception),
        });
    }

    private static Task WriteProblemAsync(HttpContext context, int status, string type, string title, string? detail)
    {
        context.Request.HttpContext.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Type = type,
            Title = title,
            Detail = detail,
            Instance = $"{context.Request.Method} {context.Request.Path}"
        }, cancellationToken: context.RequestAborted);
    }

    /// <summary>
    /// The body the refresh endpoint's own <c>Unauthorized()</c> gets from MVC: the client-error
    /// ProblemDetails for 401 from the registered factory, written with MVC's JSON options and
    /// content type, so the two cannot differ. Not logged, as the endpoint does not log it.
    /// </summary>
    private static Task WriteRefreshAlreadyRotatedAsync(HttpContext context)
    {
        const int status = (int)HttpStatusCode.Unauthorized;
        ProblemDetails problem = context.RequestServices.GetRequiredService<ProblemDetailsFactory>()
            .CreateProblemDetails(context, status);
        JsonSerializerOptions json = context.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value
            .JsonSerializerOptions;
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(problem, json, "application/problem+json; charset=utf-8",
            context.RequestAborted);
    }

    private Task WriteAccountValueRefusedAsync(HttpContext context, Exception exception)
    {
        _logger.LogError(exception, "An account row was refused by a check constraint");
        return WriteProblemAsync(context, (int)HttpStatusCode.BadRequest, "BusinessException", "Client error",
            AccountValueRefused);
    }

    private Task WriteDatabaseUnavailableAsync(HttpContext context, Exception exception)
    {
        context.Request.HttpContext.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
        _logger.LogError(exception, "Database unavailable");
        return WriteServiceUnavailableAsync(context);
    }

    private Task WriteCacheUnavailableAsync(HttpContext context, Exception exception)
    {
        context.Request.HttpContext.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
        _logger.LogError(exception, "Cache unavailable");
        return WriteServiceUnavailableAsync(context);
    }

    private Task WriteUnexpectedErrorAsync(HttpContext context, Exception exception)
    {
        _logger.LogError(exception, "An unexpected error occurred");
        return WriteProblemAsync(context, (int)HttpStatusCode.InternalServerError, "ServerError",
            "An unexpected error occurred", "An unexpected error occurred");
    }
}
