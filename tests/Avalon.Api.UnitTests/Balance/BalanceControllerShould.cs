using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Avalon.Api.Balance;
using Avalon.Api.UnitTests.Authentication;
using Avalon.Balance.Contract;
using Avalon.Common.Accounts;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Balance;

public sealed class BalanceControllerShould
{
    public static TheoryData<string, string, string> Routes => new()
    {
        { "GET", "/balance/catalog", "" },
        { "POST", "/balance/runs", "{}" },
        { "GET", "/balance/runs/r1", "" },
        { "DELETE", "/balance/runs/r1", "" },
        { "POST", "/balance/exports", """{"title":"t","overrides":{}}""" },
    };

    private static readonly CatalogDto Catalog = new("1.0.0", "abc", [], new BalanceConfigDto("{}", "{}", "{}"), ["Warrior"],
        [1], [], [], [], []);

    private static readonly AccountAccessLevel Admin = AccountAccessLevel.Player | AccountAccessLevel.Admin;

    private static async Task<HttpResponseMessage> SendAsync(ApiAuthHost host, string method, string path, string body,
        AccountAccessLevel? level)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (level is { } l)
        {
            var account = ApiAuthHost.MakeAccount(l);
            host.AccountNowIs(account);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiAuthHost.Mint(account));
        }
        if (body.Length > 0) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return await host.Client.SendAsync(request);
    }

    private static Task<ApiAuthHost> Host(IBalanceClient client) =>
        ApiAuthHost.StartAsync(configure: s =>
        {
            s.AddSingleton(client);
            // As Program.cs: the API's own serializer drops nulls, which the forwarded bodies must keep.
            s.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(o =>
                o.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull);
        });

    [Fact]
    public async Task Let_an_admin_read_the_catalog()
    {
        IBalanceClient client = Substitute.For<IBalanceClient>();
        client.CatalogAsync(Arg.Any<CancellationToken>()).Returns(new BalanceResponse<CatalogDto>(200, Catalog, """{"version":"1.0.0","commit":"abc"}"""));
        await using ApiAuthHost host = await Host(client);

        using HttpResponseMessage response = await SendAsync(host, "GET", "/balance/catalog", "", Admin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("abc", json.RootElement.GetProperty("commit").GetString());
    }

    [Fact]
    public async Task Answer_a_started_run_with_202_and_its_id()
    {
        IBalanceClient client = Substitute.For<IBalanceClient>();
        client.StartRunAsync(Arg.Any<RunRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(new BalanceResponse<RunAcceptedDto>(202, new RunAcceptedDto("r1"), """{"runId":"r1"}"""));
        await using ApiAuthHost host = await Host(client);

        using HttpResponseMessage response = await SendAsync(host, "POST", "/balance/runs", "{}", Admin);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Contains("\"runId\":\"r1\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Forward_a_success_body_byte_for_byte_with_its_nulls()
    {
        const string raw = """{"runId":"r1","status":"running","rowsDone":0,"rowsTotal":4,"result":null,"issues":[]}""";
        IBalanceClient client = Substitute.For<IBalanceClient>();
        client.GetRunAsync("r1", Arg.Any<CancellationToken>())
            .Returns(new BalanceResponse<RunStatusDto>(200, new RunStatusDto("r1", "running", 0, 4, null, []), raw));
        await using ApiAuthHost host = await Host(client);

        using HttpResponseMessage response = await SendAsync(host, "GET", "/balance/runs/r1", "", Admin);

        Assert.Equal(raw, await response.Content.ReadAsStringAsync());
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Answer_a_cancelled_run_with_204()
    {
        IBalanceClient client = Substitute.For<IBalanceClient>();
        client.CancelRunAsync("r1", Arg.Any<CancellationToken>()).Returns(new BalanceResponse(204, null));
        await using ApiAuthHost host = await Host(client);

        using HttpResponseMessage response = await SendAsync(host, "DELETE", "/balance/runs/r1", "", Admin);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Refuse_a_player_with_403(string method, string path, string body)
    {
        await using ApiAuthHost host = await Host(Substitute.For<IBalanceClient>());

        using HttpResponseMessage response = await SendAsync(host, method, path, body, AccountAccessLevel.Player);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Refuse_an_anonymous_caller_with_401(string method, string path, string body)
    {
        await using ApiAuthHost host = await Host(Substitute.For<IBalanceClient>());

        using HttpResponseMessage response = await SendAsync(host, method, path, body, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private const string IssuesBody = """{"runId":"","status":"invalid","rowsDone":0,"rowsTotal":0,"result":null,"issues":[{"path":"overrides.x","message":"unknown key"}]}""";
    private const string ProblemBody = """{"status":502,"title":"Bad gateway","detail":"github said no"}""";

    [Theory]
    [InlineData("POST", "/balance/runs", "{}", 422, IssuesBody)]
    [InlineData("POST", "/balance/runs", "{}", 429, null)]
    [InlineData("GET", "/balance/runs/r1", "", 404, null)]
    [InlineData("DELETE", "/balance/runs/r1", "", 404, null)]
    [InlineData("POST", "/balance/exports", """{"title":"t","overrides":{}}""", 422, IssuesBody)]
    [InlineData("POST", "/balance/exports", """{"title":"t","overrides":{}}""", 502, ProblemBody)]
    [InlineData("POST", "/balance/exports", """{"title":"t","overrides":{}}""", 503, ProblemBody)]
    public async Task Pass_the_services_status_and_body_through_unchanged(string method, string path, string body,
        int status, string? json)
    {
        IBalanceClient client = Substitute.For<IBalanceClient>();
        client.StartRunAsync(default!, default).ReturnsForAnyArgs(new BalanceResponse<RunAcceptedDto>(status, null, json));
        client.GetRunAsync(default!, default).ReturnsForAnyArgs(new BalanceResponse<RunStatusDto>(status, null, json));
        client.CancelRunAsync(default!, default).ReturnsForAnyArgs(new BalanceResponse(status, json));
        client.ExportAsync(default!, default).ReturnsForAnyArgs(new BalanceResponse<ExportResultDto>(status, null, json));
        await using ApiAuthHost host = await Host(client);

        using HttpResponseMessage response = await SendAsync(host, method, path, body, Admin);

        Assert.Equal(status, (int)response.StatusCode);
        // A status with no body of its own gets MVC's standard ProblemDetails for that status.
        if (json is not null) Assert.Equal(json, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Answer_503_when_the_service_is_not_configured(string method, string path, string body)
    {
        await using ApiAuthHost host = await ApiAuthHost.StartAsync(
            configure: s => s.AddSingleton<IBalanceClient, UnconfiguredBalanceClient>());

        using HttpResponseMessage response = await SendAsync(host, method, path, body, Admin);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("balance service not configured", json.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Answer_503_when_the_service_is_unreachable()
    {
        IBalanceClient client = Substitute.For<IBalanceClient>();
        client.CatalogAsync(Arg.Any<CancellationToken>())
            .Returns<BalanceResponse<CatalogDto>>(_ => throw new BalanceUnavailableException("balance service unavailable"));
        await using ApiAuthHost host = await Host(client);

        using HttpResponseMessage response = await SendAsync(host, "GET", "/balance/catalog", "", Admin);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("balance service unavailable", json.RootElement.GetProperty("detail").GetString());
    }
}
