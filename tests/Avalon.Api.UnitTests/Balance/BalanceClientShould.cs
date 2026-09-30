using System.Net;
using System.Text;
using System.Text.Json;
using Avalon.Api.Balance;
using Avalon.Balance.Contract;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Avalon.Api.UnitTests.Balance;

public sealed class BalanceClientShould
{
    private const string Secret = "s3cret-value";

    private sealed class StubHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string?> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            int call = Interlocked.Increment(ref Calls);
            Requests.Add(request);
            Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(ct));
            return respond(request, call);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>The client as production builds it: the standard handler every client gets, then ours.</summary>
    private static IBalanceClient Build(StubHandler handler)
    {
        var services = new ServiceCollection();
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
        services.AddBalanceClient(new BalanceConfiguration { Url = "http://balance.test/", SharedSecret = Secret },
                retryDelay: TimeSpan.FromMilliseconds(1))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<IBalanceClient>();
    }

    private static readonly RunRequestDto Run = new(null, null, null, null, null);

    [Fact]
    public async Task Not_retry_a_post_that_failed()
    {
        var handler = new StubHandler((_, call) => call == 1
            ? Json(HttpStatusCode.InternalServerError, "{}")
            : Json(HttpStatusCode.Accepted, """{"runId":"r1"}"""));

        BalanceResponse<RunAcceptedDto> response = await Build(handler).StartRunAsync(Run, default);

        Assert.Equal(500, response.Status);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Not_retry_a_post_that_could_not_connect()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("refused"));

        await Assert.ThrowsAsync<BalanceUnavailableException>(() => Build(handler).StartRunAsync(Run, default));

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Not_retry_a_delete_that_failed()
    {
        var handler = new StubHandler((_, call) => call == 1
            ? Json(HttpStatusCode.ServiceUnavailable, "{}")
            : new HttpResponseMessage(HttpStatusCode.NoContent));

        BalanceResponse response = await Build(handler).CancelRunAsync("r1", default);

        Assert.Equal(503, response.Status);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Retry_a_get_that_failed_with_503()
    {
        var handler = new StubHandler((_, call) => call == 1
            ? Json(HttpStatusCode.ServiceUnavailable, "{}")
            : Json(HttpStatusCode.OK, """{"runId":"r1","status":"done","rowsDone":1,"rowsTotal":1,"result":null,"issues":[]}"""));

        BalanceResponse<RunStatusDto> response = await Build(handler).GetRunAsync("r1", default);

        Assert.Equal(200, response.Status);
        Assert.Equal("done", response.Value!.Status);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Retry_a_get_that_could_not_connect_then_answer_unavailable()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("refused"));

        var ex = await Assert.ThrowsAsync<BalanceUnavailableException>(() => Build(handler).GetRunAsync("r1", default));

        Assert.True(handler.Calls > 1);
        Assert.Equal("balance service unavailable", ex.Message);
    }

    [Fact]
    public async Task Send_the_secret_header_and_never_put_it_in_an_exception()
    {
        var failing = new StubHandler((_, _) => throw new HttpRequestException("refused"));
        var ok = new StubHandler((_, _) => Json(HttpStatusCode.OK, "{}"));

        await Build(ok).GetRunAsync("r1", default);
        var ex = await Assert.ThrowsAsync<BalanceUnavailableException>(() => Build(failing).GetRunAsync("r1", default));

        Assert.Equal(Secret, Assert.Single(ok.Requests[0].Headers.GetValues("X-Balance-Secret")));
        Assert.DoesNotContain(Secret, ex.ToString());
    }

    [Fact]
    public async Task Answer_502_when_the_service_rejects_the_secret()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        BalanceResponse<RunStatusDto> response = await Build(handler).GetRunAsync("r1", default);

        Assert.Equal(502, response.Status);
        using JsonDocument json = JsonDocument.Parse(response.Json!);
        Assert.Equal("balance service rejected the API's credentials", json.RootElement.GetProperty("detail").GetString());
        Assert.DoesNotContain(Secret, response.Json);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Keep_a_422_body_with_its_issues()
    {
        const string body = """{"runId":"","status":"invalid","rowsDone":0,"rowsTotal":0,"result":null,"issues":[{"path":"a","message":"b"}]}""";
        var handler = new StubHandler((_, _) => Json((HttpStatusCode)422, body));

        BalanceResponse<RunAcceptedDto> response = await Build(handler).StartRunAsync(Run, default);

        Assert.Equal(422, response.Status);
        Assert.Equal(body, response.Json);
    }

    [Fact]
    public async Task Send_the_request_in_the_shared_wire_format_to_the_services_path()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.Accepted, """{"runId":"r1"}"""));

        await Build(handler).StartRunAsync(new RunRequestDto(null, null, null, 5, 7), default);

        Assert.Contains("\"runsPerRow\":5", handler.Bodies[0]);
        Assert.Equal("http://balance.test/runs", handler.Requests[0].RequestUri!.ToString());
    }

    [Theory]
    [InlineData("", "", false)]
    [InlineData("http://x", "", false)]
    [InlineData("", "s", false)]
    [InlineData("http://x", "s", true)]
    public void Be_configured_only_with_a_url_and_a_secret(string url, string secret, bool expected) =>
        Assert.Equal(expected, new BalanceConfiguration { Url = url, SharedSecret = secret }.IsConfigured);
}
