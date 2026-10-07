using System.Data.Common;
using System.Text.Json;
using Avalon.Api.Hosting.Middlewares;
using Avalon.Api.Identity.Exceptions;
using Avalon.Api.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Avalon.Api.Hosting.UnitTests.Middlewares;

/// <summary>
/// #480: a 503 says the service is unavailable and nothing more. The exception's type (which names
/// the database or cache driver) and its message (which can carry hosts and ports) stay in the log.
/// </summary>
public class ExceptionHandlerMiddlewareShould
{
    // A connection string as a driver might echo it, built at run time: no literal credential in source.
    private static readonly string s_secret = "postgres-server:5432 password=" + TestPasswords.Valid;

    public static TheoryData<Exception> Outages => new()
    {
        new FakeDbException(s_secret),
        new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.CommandRetryNever, s_secret),
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
        using var json = JsonDocument.Parse(body);
        Assert.Equal("ServiceUnavailable", json.RootElement.GetProperty("type").GetString());
        Assert.DoesNotContain(outage.GetType().Name, body, StringComparison.Ordinal);
        Assert.DoesNotContain(TestPasswords.Valid, body, StringComparison.Ordinal);
        Assert.DoesNotContain("5432", body, StringComparison.Ordinal);
    }

    /// <summary>Game distribution: no store configured, or a build that is not there, is a 503 that says so.</summary>
    [Fact]
    public async Task Answer_downloads_that_are_not_available_with_503()
    {
        var middleware = new ExceptionHandlerMiddleware(
            _ => throw new Avalon.Api.Distribution.DistributionUnavailableException("Downloads are not available right now."),
            NullLoggerFactory.Instance, [new Avalon.Api.Distribution.DistributionProblemMapper()]);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var json = JsonDocument.Parse(await new StreamReader(context.Response.Body).ReadToEndAsync());
        Assert.Equal("Downloads are not available right now.", json.RootElement.GetProperty("detail").GetString());
    }

    /// <summary>#510: an email-change confirmation the sender could not send is a 503 that says so.</summary>
    [Fact]
    public async Task Answer_an_email_that_could_not_be_sent_with_503()
    {
        var middleware = new ExceptionHandlerMiddleware(_ => throw new Avalon.Api.Identity.Exceptions.EmailDeliveryException(),
            NullLoggerFactory.Instance, [new IdentityProblemMapper()]);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var json = JsonDocument.Parse(await new StreamReader(context.Response.Body).ReadToEndAsync());
        Assert.Equal(503, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Email could not be sent", json.RootElement.GetProperty("detail").GetString());
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

    /// <summary>
    /// Only the two normalisation constraints are the caller's value. Any other check violation,
    /// even on Accounts, is not mapped to 400: the database refused something the code chose.
    /// </summary>
    [Theory]
    [InlineData("Accounts", "CK_Accounts_Something_Else")]
    [InlineData("Characters", "CK_Accounts_Email_Normalised")]
    public async Task Not_answer_another_check_violation_with_400(string table, string constraint)
    {
        var violation = new Npgsql.PostgresException("new row violates check constraint", "ERROR", "ERROR",
            Npgsql.PostgresErrorCodes.CheckViolation, tableName: table, constraintName: constraint);
        var middleware = new ExceptionHandlerMiddleware(_ => throw violation, NullLoggerFactory.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.NotEqual(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task Answer_a_username_normalisation_violation_with_400()
    {
        var violation = new Npgsql.PostgresException("new row violates check constraint", "ERROR", "ERROR",
            Npgsql.PostgresErrorCodes.CheckViolation, tableName: "Accounts",
            constraintName: Avalon.Database.Auth.AuthDbContext.UsernameNormalisedConstraint);
        var middleware = new ExceptionHandlerMiddleware(_ => throw violation, NullLoggerFactory.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    private sealed class FakeDbException(string message) : DbException(message);

    [Fact]
    public async Task Write_nothing_when_the_caller_hung_up()
    {
        using var aborted = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = aborted.Token };
        context.Response.Body = new MemoryStream();
        aborted.Cancel();
        var middleware = new ExceptionHandlerMiddleware(_ => throw new OperationCanceledException(aborted.Token),
            NullLoggerFactory.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(0, context.Response.Body.Length);
        Assert.NotEqual(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
    }

    [Fact]
    public async Task Still_answer_500_for_a_cancellation_the_caller_did_not_cause()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ExceptionHandlerMiddleware(_ => throw new OperationCanceledException(),
            NullLoggerFactory.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
    }
}
