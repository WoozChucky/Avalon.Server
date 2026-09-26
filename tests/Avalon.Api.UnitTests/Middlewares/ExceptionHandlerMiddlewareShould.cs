using System.Data.Common;
using System.Text.Json;
using Avalon.Api.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Avalon.Api.UnitTests.Middlewares;

/// <summary>
/// #480: a 503 says the service is unavailable and nothing more. The exception's type (which names
/// the database or cache driver) and its message (which can carry hosts and ports) stay in the log.
/// </summary>
public class ExceptionHandlerMiddlewareShould
{
    private const string Secret = "postgres-server:5432 password=hunter2";

    public static TheoryData<Exception> Outages => new()
    {
        new FakeDbException(Secret),
        new RedisConnectionException(ConnectionFailureType.UnableToConnect, Secret),
    };

    [Theory]
    [MemberData(nameof(Outages))]
    public async Task Answer_an_outage_with_a_fixed_503_that_names_no_driver(Exception outage)
    {
        var middleware = new ExceptionHandlerMiddleware(_ => throw outage, NullLoggerFactory.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        string body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        using JsonDocument json = JsonDocument.Parse(body);
        Assert.Equal("ServiceUnavailable", json.RootElement.GetProperty("type").GetString());
        Assert.DoesNotContain(outage.GetType().Name, body, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", body, StringComparison.Ordinal);
        Assert.DoesNotContain("5432", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// #503 follow-up: a row an Accounts check constraint refuses (a username or an email not in
    /// its stored form) is the caller's value, not an outage: 400, whether it comes straight from a
    /// bulk update or wrapped by SaveChanges.
    /// </summary>
    public static TheoryData<Exception> CheckViolations => new()
    {
        AccountsCheckViolation(),
        new Microsoft.EntityFrameworkCore.DbUpdateException("save failed", AccountsCheckViolation()),
    };

    private static Npgsql.PostgresException AccountsCheckViolation() => new(
        "new row violates check constraint", "ERROR", "ERROR", Npgsql.PostgresErrorCodes.CheckViolation,
        tableName: "Accounts", constraintName: "CK_Accounts_Email_Normalised");

    [Theory]
    [MemberData(nameof(CheckViolations))]
    public async Task Answer_a_check_constraint_violation_on_accounts_with_400(Exception violation)
    {
        var middleware = new ExceptionHandlerMiddleware(_ => throw violation, NullLoggerFactory.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        string body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.DoesNotContain("CK_Accounts", body, StringComparison.Ordinal);
    }

    private sealed class FakeDbException(string message) : DbException(message);
}
