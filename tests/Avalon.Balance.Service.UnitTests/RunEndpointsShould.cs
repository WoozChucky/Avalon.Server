using System.Net;
using System.Net.Http.Json;
using System.Text;
using Avalon.Balance.Contract;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Avalon.Balance.Service.UnitTests;

public class RunEndpointsShould
{
    private static readonly Dictionary<string, string?> Paused = new() { ["Balance:RunWorker"] = "false" };

    private const string SmallRun =
        """{"filter":{"classes":["Warrior"],"scenarios":["normal-3"]},"runsPerRow":10}""";

    private static HttpClient Client(WebApplication app)
    {
        HttpClient client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Balance-Secret", BalanceTestHost.Secret);
        return client;
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    [Fact]
    public async Task Accept_a_run_and_answer_its_status_until_done_with_the_result()
    {
        await using WebApplication app = BalanceTestHost.Build();
        await app.StartAsync();
        HttpClient client = Client(app);

        HttpResponseMessage accepted = await client.PostAsync("/runs", Json(SmallRun));
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        RunAcceptedDto? run = await accepted.Content.ReadFromJsonAsync<RunAcceptedDto>(BalanceJson.Options);
        Assert.NotNull(run);

        RunStatusDto? status;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        do
        {
            await Task.Delay(10, timeout.Token);
            HttpResponseMessage response = await client.GetAsync($"/runs/{run.RunId}", timeout.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            status = await response.Content.ReadFromJsonAsync<RunStatusDto>(BalanceJson.Options, timeout.Token);
        } while (status!.Status is "queued" or "running");

        Assert.Equal("done", status.Status);
        Assert.Equal(run.RunId, status.RunId);
        Assert.NotNull(status.Result);
        Assert.NotEmpty(status.Result.Rows);
        Assert.Equal(status.RowsTotal, status.RowsDone);
    }

    [Fact]
    public async Task Answer_422_with_the_issues_for_a_refused_request()
    {
        await using WebApplication app = BalanceTestHost.Build(extra: Paused);
        await app.StartAsync();

        HttpResponseMessage response = await Client(app).PostAsync("/runs", Json("""{"runsPerRow":1001}"""));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        RunStatusDto? status = await response.Content.ReadFromJsonAsync<RunStatusDto>(BalanceJson.Options);
        Assert.Equal("invalid", status!.Status);
        Assert.Contains(status.Issues, i => i.Path == "runsPerRow");
    }

    [Fact]
    public async Task Answer_400_for_a_body_that_is_not_json()
    {
        await using WebApplication app = BalanceTestHost.Build(extra: Paused);
        await app.StartAsync();

        HttpResponseMessage response = await Client(app).PostAsync("/runs", Json("{not json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Answer_429_for_the_fifth_run()
    {
        await using WebApplication app = BalanceTestHost.Build(extra: Paused);
        await app.StartAsync();
        HttpClient client = Client(app);

        for (int i = 0; i < 4; i++)
            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/runs", Json(SmallRun))).StatusCode);
        HttpResponseMessage fifth = await client.PostAsync("/runs", Json(SmallRun));

        Assert.Equal(HttpStatusCode.TooManyRequests, fifth.StatusCode);
    }

    [Fact]
    public async Task Answer_404_for_an_unknown_run()
    {
        await using WebApplication app = BalanceTestHost.Build(extra: Paused);
        await app.StartAsync();
        HttpClient client = Client(app);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/runs/unknown")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/runs/unknown")).StatusCode);
    }

    [Fact]
    public async Task Cancel_a_queued_run_with_204()
    {
        await using WebApplication app = BalanceTestHost.Build(extra: Paused);
        await app.StartAsync();
        HttpClient client = Client(app);
        RunAcceptedDto? run = await (await client.PostAsync("/runs", Json(SmallRun)))
            .Content.ReadFromJsonAsync<RunAcceptedDto>(BalanceJson.Options);

        HttpResponseMessage deleted = await client.DeleteAsync($"/runs/{run!.RunId}");
        RunStatusDto? status = await (await client.GetAsync($"/runs/{run.RunId}"))
            .Content.ReadFromJsonAsync<RunStatusDto>(BalanceJson.Options);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal("cancelled", status!.Status);
    }

    [Fact]
    public async Task Require_the_secret_for_runs()
    {
        await using WebApplication app = BalanceTestHost.Build(extra: Paused);
        await app.StartAsync();

        HttpResponseMessage response = await app.GetTestClient().PostAsync("/runs", Json(SmallRun));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refuse_a_body_over_1_MiB_with_413_on_the_real_Kestrel()
    {
        await using WebApplication app = BalanceTestHost.BuildKestrel();
        await app.StartAsync();
        string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        client.DefaultRequestHeaders.Add("X-Balance-Secret", BalanceTestHost.Secret);
        string padding = new('x', 1024 * 1024 + 1024);

        HttpResponseMessage response = await client.PostAsync("/runs", Json($$"""{"seed":1,"pad":"{{padding}}"}"""));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }
}
