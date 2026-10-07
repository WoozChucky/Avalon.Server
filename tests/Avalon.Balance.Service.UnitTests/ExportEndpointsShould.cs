using System.Net;
using System.Net.Http.Json;
using System.Text;
using Avalon.Balance.Contract;
using Avalon.Balance.Core;
using Avalon.Balance.Service.Export;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Avalon.Balance.Service.UnitTests;

public class ExportEndpointsShould
{
    private const string Token = "github_pat_SECRET_TOKEN_VALUE";
    private const string Edit = """{"title":"Slam tuning","notes":"n","overrides":{"Ability.201.EffectValue":18}}""";

    private static readonly Dictionary<string, string?> s_withToken = new() { ["Balance:GitHubToken"] = Token };

    private static WebApplication Build(FakeGitHub github, bool token = true, string? commit = FakeGitHub.Commit,
        Dictionary<string, string?>? extra = null) =>
        BalanceTestHost.Build(
            extra: extra ?? (token ? s_withToken : null),
            services: s =>
            {
                s.AddSingleton<IGitHub>(github);
                s.AddSingleton(new BuildInfo(commit));
                s.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 9, 30, 14, 5, 0, TimeSpan.Zero)));
            });

    private static HttpClient Client(WebApplication app)
    {
        HttpClient client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Balance-Secret", BalanceTestHost.Secret);
        return client;
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static FakeGitHub GitHubWithDefaults(BalanceHost host)
    {
        var github = new FakeGitHub();
        (string scenarios, string targets, string rotations) = ConfigFiles.Save(host.Defaults);
        github.FilesAtCommit["balance/overrides.json"] = ("s1", "{}\n");
        github.FilesAtCommit["balance/scenarios.json"] = ("s2", scenarios);
        github.FilesAtCommit["balance/targets.json"] = ("s3", targets);
        github.FilesAtCommit["balance/rotations.json"] = ("s4", rotations);
        return github;
    }

    [Fact]
    public async Task Answer_200_with_the_pull_request_url_and_branch()
    {
        var github = new FakeGitHub();
        await using WebApplication app = Build(github);
        FakeGitHub seeded = GitHubWithDefaults(app.Services.GetRequiredService<BalanceHost>());
        foreach (KeyValuePair<string, (string Sha, string Text)> file in seeded.FilesAtCommit)
            github.FilesAtCommit[file.Key] = file.Value;
        await app.StartAsync();

        HttpResponseMessage response = await Client(app).PostAsync("/exports", Json(Edit));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ExportResultDto? result = await response.Content.ReadFromJsonAsync<ExportResultDto>(BalanceJson.Options);
        Assert.Equal("https://github.com/WoozChucky/Avalon.Server/pull/999", result!.PullRequestUrl);
        Assert.Equal("balance/slam-tuning-20260930-1405", result.Branch);
        Assert.Equal("balance/overrides.json", Assert.Single(github.Puts).Path);
    }

    [Fact]
    public async Task Answer_503_without_calling_github_when_no_token_is_configured()
    {
        var github = new FakeGitHub();
        await using WebApplication app = Build(github, token: false);
        await app.StartAsync();

        // An invalid body too: the missing token is checked before the body is validated.
        HttpResponseMessage response = await Client(app).PostAsync("/exports", Json("""{"title":""}"""));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(github.Calls);
    }

    [Fact]
    public async Task Answer_503_without_calling_github_when_the_service_has_no_build_commit()
    {
        var github = new FakeGitHub();
        await using WebApplication app = Build(github, commit: null);
        await app.StartAsync();

        HttpResponseMessage response = await Client(app).PostAsync("/exports", Json(Edit));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("commit", await response.Content.ReadAsStringAsync());
        Assert.Empty(github.Calls);
    }

    [Fact]
    public async Task Answer_422_with_the_issues_for_an_invalid_override()
    {
        var github = new FakeGitHub();
        await using WebApplication app = Build(github);
        await app.StartAsync();

        HttpResponseMessage response = await Client(app).PostAsync("/exports",
            Json("""{"title":"t","overrides":{"Ability.999.EffectValue":1}}"""));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        RunStatusDto? status = await response.Content.ReadFromJsonAsync<RunStatusDto>(BalanceJson.Options);
        Assert.Equal("invalid", status!.Status);
        Assert.Contains(status.Issues, i => i.Path.StartsWith("overrides", StringComparison.Ordinal));
        Assert.Empty(github.Calls);
    }

    [Fact]
    public async Task Answer_422_for_more_overrides_than_allowed()
    {
        var github = new FakeGitHub();
        await using WebApplication app = Build(github, extra: new Dictionary<string, string?>
        {
            ["Balance:GitHubToken"] = Token,
            ["Balance:MaxOverrides"] = "1",
        });
        await app.StartAsync();

        HttpResponseMessage response = await Client(app).PostAsync("/exports",
            Json("""{"title":"t","overrides":{"Ability.201.EffectValue":1,"Ability.200.EffectValue":2}}"""));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Empty(github.Calls);
    }

    [Fact]
    public async Task Answer_400_for_a_body_that_is_not_json()
    {
        await using WebApplication app = Build(new FakeGitHub());
        await app.StartAsync();

        HttpResponseMessage response = await Client(app).PostAsync("/exports", Json("{not json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Answer_502_naming_the_step_and_status_but_not_the_token()
    {
        var github = new FakeGitHub { Fail = ("CreateBranch", 403) };
        await using WebApplication app = Build(github);
        FakeGitHub seeded = GitHubWithDefaults(app.Services.GetRequiredService<BalanceHost>());
        foreach (KeyValuePair<string, (string Sha, string Text)> file in seeded.FilesAtCommit)
            github.FilesAtCommit[file.Key] = file.Value;
        await app.StartAsync();

        HttpResponseMessage response = await Client(app).PostAsync("/exports", Json(Edit));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        string text = await response.Content.ReadAsStringAsync();
        Assert.Contains("create branch", text);
        Assert.Contains("403", text);
        Assert.DoesNotContain(Token, text);
    }

    [Fact]
    public async Task Include_the_summary_of_a_finished_run_in_the_pull_request_body()
    {
        var github = new FakeGitHub();
        await using WebApplication app = Build(github, extra: new Dictionary<string, string?>
        {
            ["Balance:GitHubToken"] = Token,
        });
        FakeGitHub seeded = GitHubWithDefaults(app.Services.GetRequiredService<BalanceHost>());
        foreach (KeyValuePair<string, (string Sha, string Text)> file in seeded.FilesAtCommit)
            github.FilesAtCommit[file.Key] = file.Value;
        await app.StartAsync();
        HttpClient client = Client(app);

        HttpResponseMessage accepted = await client.PostAsync("/runs",
            Json("""{"filter":{"classes":["Warrior"],"scenarios":["normal-3"]},"runsPerRow":10}"""));
        RunAcceptedDto run = (await accepted.Content.ReadFromJsonAsync<RunAcceptedDto>(BalanceJson.Options))!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        RunStatusDto? status;
        do
        {
            await Task.Delay(10, timeout.Token);
            status = await client.GetFromJsonAsync<RunStatusDto>($"/runs/{run.RunId}", BalanceJson.Options, timeout.Token);
        } while (status!.Status is "queued" or "running");
        Assert.Equal("done", status.Status);

        HttpResponseMessage response = await client.PostAsync("/exports",
            Json($$"""{"title":"Slam tuning","overrides":{"Ability.201.EffectValue":18},"runId":"{{run.RunId}}"}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"green {status.Result!.Summary.Green}", github.PullRequests.Single().Body);
    }
}
