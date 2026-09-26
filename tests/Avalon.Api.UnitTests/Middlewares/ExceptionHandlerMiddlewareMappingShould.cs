using System.Data.Common;
using System.Security.Authentication;
using System.Text.Json;
using Avalon.Api.Exceptions;
using Avalon.Api.Middlewares;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Xunit;

namespace Avalon.Api.UnitTests.Middlewares;

/// <summary>
/// #534: pins every mapping of <see cref="ExceptionHandlerMiddleware"/> as it stood before its
/// split: the status, the whole ProblemDetails body, and what is logged at which level. A subtype
/// takes its base type's case, and the cases are matched in their fixed order.
/// </summary>
public class ExceptionHandlerMiddlewareMappingShould
{
    private const string ServiceUnavailableDetail = "The service is temporarily unavailable. Try again shortly.";

    public static TheoryData<Exception, int, string, string, string, string?> Mappings => new()
    {
        { new AuthenticationException("Invalid credentials"), 401, "AuthenticationException", "Whoops!", "Invalid credentials", null },
        { new InvalidCredentialException("Bad token"), 401, "InvalidCredentialException", "Whoops!", "Bad token", null },
        { new AccountInactiveException(AccountStatus.Banned), 403, "AccountInactiveException", "Account not active", "BANNED", null },
        { new AccountInactiveException(AccountStatus.Deactivated), 403, "AccountInactiveException", "Account not active", "DEACTIVATED", null },
        { new AccountLockedException(), 429, "AccountLockedException", "Too many attempts", "LOCKED", null },
        { new EmailDeliveryException(), 503, "ServiceUnavailable", "Service unavailable", "Email could not be sent", null },
        { new BusinessException("Username already exists"), 400, "BusinessException", "Client error", "Username already exists", null },
        { new DerivedBusinessException("Nope"), 400, "DerivedBusinessException", "Client error", "Nope", null },
        {
            AccountsCheckViolation(), 400, "BusinessException", "Client error",
            ExceptionHandlerMiddleware.AccountValueRefused, "An account row was refused by a check constraint"
        },
        {
            new DbUpdateException("save failed", AccountsCheckViolation()), 400, "BusinessException", "Client error",
            ExceptionHandlerMiddleware.AccountValueRefused, "An account row was refused by a check constraint"
        },
        { new FakeDbException("down"), 503, "ServiceUnavailable", "Service unavailable", ServiceUnavailableDetail, "Database unavailable" },
        { OtherCheckViolation(), 503, "ServiceUnavailable", "Service unavailable", ServiceUnavailableDetail, "Database unavailable" },
        { new RetryLimitExceededException("retries spent"), 503, "ServiceUnavailable", "Service unavailable", ServiceUnavailableDetail, "Database unavailable" },
        {
            new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"), 503, "ServiceUnavailable",
            "Service unavailable", ServiceUnavailableDetail, "Cache unavailable"
        },
        {
            new DbUpdateException("save failed", OtherCheckViolation()), 500, "ServerError",
            "An unexpected error occurred", "An unexpected error occurred", "An unexpected error occurred"
        },
        {
            new InvalidOperationException("boom"), 500, "ServerError", "An unexpected error occurred",
            "An unexpected error occurred", "An unexpected error occurred"
        },
    };

    [Theory]
    [MemberData(nameof(Mappings))]
    public async Task Map_each_exception_as_before(Exception exception, int status, string type, string title,
        string detail, string? loggedError)
    {
        var logs = new CapturingLoggerFactory();
        var middleware = new ExceptionHandlerMiddleware(_ => throw exception, logs);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/account/login";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", context.Response.ContentType);
        context.Response.Body.Position = 0;
        using JsonDocument json = JsonDocument.Parse(await new StreamReader(context.Response.Body).ReadToEndAsync());
        JsonElement root = json.RootElement;
        Assert.Equal(status, root.GetProperty("status").GetInt32());
        Assert.Equal(type, root.GetProperty("type").GetString());
        Assert.Equal(title, root.GetProperty("title").GetString());
        Assert.Equal(detail, root.GetProperty("detail").GetString());
        Assert.Equal("POST /account/login", root.GetProperty("instance").GetString());

        if (loggedError is null)
        {
            Assert.Empty(logs.Entries);
        }
        else
        {
            (LogLevel level, string message, Exception? logged) = Assert.Single(logs.Entries);
            Assert.Equal(LogLevel.Error, level);
            Assert.Equal(loggedError, message);
            Assert.Same(exception, logged);
        }
    }

    private static Npgsql.PostgresException AccountsCheckViolation() => new(
        "new row violates check constraint", "ERROR", "ERROR", Npgsql.PostgresErrorCodes.CheckViolation,
        tableName: "Accounts", constraintName: "CK_Accounts_Username_Normalised");

    private static Npgsql.PostgresException OtherCheckViolation() => new(
        "new row violates check constraint", "ERROR", "ERROR", Npgsql.PostgresErrorCodes.CheckViolation,
        tableName: "Characters", constraintName: "CK_Characters_Something");

    private sealed class FakeDbException(string message) : DbException(message);

    private sealed class DerivedBusinessException(string message) : BusinessException(message);

    private sealed class CapturingLoggerFactory : ILoggerFactory, ILogger
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public void AddProvider(ILoggerProvider provider) { }

        public void Dispose() { }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
