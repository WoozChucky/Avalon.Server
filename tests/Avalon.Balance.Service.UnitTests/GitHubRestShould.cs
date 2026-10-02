using System.Net;
using System.Text;
using System.Text.Json;
using Avalon.Balance.Service.Export;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Avalon.Balance.Service.UnitTests;

public class GitHubRestShould
{
    private const string Token = "github_pat_SECRET_TOKEN_VALUE";

    private sealed record Seen(HttpMethod Method, Uri Uri, HttpRequestMessage Request, string Body);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Seen> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add(new Seen(request.Method, request.RequestUri!, request, body));
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (GitHubRest Client, StubHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var http = new HttpClient(handler);
        GitHubRest.Configure(http, "WoozChucky/Avalon.Server", Token);
        return (new GitHubRest(http), handler);
    }

    [Fact]
    public async Task Read_a_file_at_a_ref_and_decode_its_base64_content()
    {
        string text = "{\n  \"a\": 1\n}\n";
        string wrapped = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).Insert(4, "\n");
        (GitHubRest client, StubHandler handler) = Create(_ => Json(HttpStatusCode.OK,
            JsonSerializer.Serialize(new { sha = "blob1", encoding = "base64", content = wrapped })));

        (string Sha, string Text)? file = await client.GetFileAsync("balance/overrides.json", "abc123", CancellationToken.None);

        Assert.Equal(("blob1", text), file);
        Seen seen = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, seen.Method);
        Assert.Equal("https://api.github.com/repos/WoozChucky/Avalon.Server/contents/balance/overrides.json?ref=abc123", seen.Uri.ToString());
    }

    [Fact]
    public async Task Send_the_token_and_the_github_headers()
    {
        (GitHubRest client, StubHandler handler) = Create(_ => Json(HttpStatusCode.NotFound, "{}"));

        await client.GetFileAsync("balance/overrides.json", "abc", CancellationToken.None);

        HttpRequestMessage request = handler.Requests.Single().Request;
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(Token, request.Headers.Authorization.Parameter);
        Assert.Contains(request.Headers.Accept, a => a.MediaType == "application/vnd.github+json");
        Assert.Equal("2022-11-28", request.Headers.GetValues("X-GitHub-Api-Version").Single());
        Assert.False(string.IsNullOrWhiteSpace(request.Headers.UserAgent.ToString()));
    }

    [Fact]
    public async Task Treat_a_404_file_as_absent()
    {
        (GitHubRest client, _) = Create(_ => Json(HttpStatusCode.NotFound, """{"message":"Not Found"}"""));

        Assert.Null(await client.GetFileAsync("balance/overrides.json", "abc", CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task Answer_whether_a_branch_exists(HttpStatusCode status, bool exists)
    {
        (GitHubRest client, StubHandler handler) = Create(_ => Json(status, "{}"));

        Assert.Equal(exists, await client.BranchExistsAsync("balance/slam-20260930-1405", CancellationToken.None));

        Seen seen = handler.Requests.Single();
        Assert.Equal(HttpMethod.Get, seen.Method);
        Assert.Equal("/repos/WoozChucky/Avalon.Server/git/ref/heads/balance/slam-20260930-1405", seen.Uri.AbsolutePath);
    }

    [Fact]
    public async Task Fail_on_a_branch_check_that_is_neither_found_nor_missing()
    {
        (GitHubRest client, _) = Create(_ => Json(HttpStatusCode.Forbidden, "{}"));

        GitHubApiException e = await Assert.ThrowsAsync<GitHubApiException>(() => client.BranchExistsAsync("b", CancellationToken.None));

        Assert.Equal(403, e.StatusCode);
    }

    [Fact]
    public async Task Create_a_branch_by_posting_the_ref_and_the_sha()
    {
        (GitHubRest client, StubHandler handler) = Create(_ => Json(HttpStatusCode.Created, "{}"));

        await client.CreateBranchAsync("balance/slam-20260930-1405", "abc123", CancellationToken.None);

        Seen seen = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, seen.Method);
        Assert.Equal("/repos/WoozChucky/Avalon.Server/git/refs", seen.Uri.AbsolutePath);
        using JsonDocument body = JsonDocument.Parse(seen.Body);
        Assert.Equal("refs/heads/balance/slam-20260930-1405", body.RootElement.GetProperty("ref").GetString());
        Assert.Equal("abc123", body.RootElement.GetProperty("sha").GetString());
    }

    [Fact]
    public async Task Put_a_file_as_base64_utf8_without_a_bom_on_the_branch_with_the_existing_sha()
    {
        (GitHubRest client, StubHandler handler) = Create(_ => Json(HttpStatusCode.OK, "{}"));

        await client.PutFileAsync("balance/b", "balance/overrides.json", "{}\n", "blob1", "chore(balance): x", CancellationToken.None);

        Seen seen = handler.Requests.Single();
        Assert.Equal(HttpMethod.Put, seen.Method);
        Assert.Equal("/repos/WoozChucky/Avalon.Server/contents/balance/overrides.json", seen.Uri.AbsolutePath);
        using JsonDocument body = JsonDocument.Parse(seen.Body);
        byte[] content = Convert.FromBase64String(body.RootElement.GetProperty("content").GetString()!);
        Assert.Equal("{}\n"u8.ToArray(), content);
        Assert.Equal("balance/b", body.RootElement.GetProperty("branch").GetString());
        Assert.Equal("blob1", body.RootElement.GetProperty("sha").GetString());
        Assert.Equal("chore(balance): x", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Leave_the_sha_out_when_putting_a_new_file()
    {
        (GitHubRest client, StubHandler handler) = Create(_ => Json(HttpStatusCode.Created, "{}"));

        await client.PutFileAsync("b", "balance/overrides.json", "{}\n", null, "m", CancellationToken.None);

        using JsonDocument body = JsonDocument.Parse(handler.Requests.Single().Body);
        Assert.False(body.RootElement.TryGetProperty("sha", out _));
    }

    [Fact]
    public async Task Open_a_draft_pull_request_against_main_and_return_its_url()
    {
        (GitHubRest client, StubHandler handler) = Create(_ => Json(HttpStatusCode.Created, """{"html_url":"https://github.com/o/r/pull/7"}"""));

        string url = await client.OpenDraftPullRequestAsync("balance/b", "chore(balance): x", "the body", CancellationToken.None);

        Assert.Equal("https://github.com/o/r/pull/7", url);
        Seen seen = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, seen.Method);
        Assert.Equal("/repos/WoozChucky/Avalon.Server/pulls", seen.Uri.AbsolutePath);
        using JsonDocument body = JsonDocument.Parse(seen.Body);
        Assert.True(body.RootElement.GetProperty("draft").GetBoolean());
        Assert.Equal("main", body.RootElement.GetProperty("base").GetString());
        Assert.Equal("balance/b", body.RootElement.GetProperty("head").GetString());
        Assert.Equal("chore(balance): x", body.RootElement.GetProperty("title").GetString());
        Assert.Equal("the body", body.RootElement.GetProperty("body").GetString());
    }

    [Fact]
    public async Task Throw_a_message_without_the_token_or_the_headers_or_the_response_body_on_a_422()
    {
        (GitHubRest client, _) = Create(_ => Json(HttpStatusCode.UnprocessableEntity,
            $$"""{"message":"Validation Failed echo {{Token}} Authorization: Bearer {{Token}}"}"""));

        GitHubApiException e = await Assert.ThrowsAsync<GitHubApiException>(() =>
            client.OpenDraftPullRequestAsync("b", "t", "body", CancellationToken.None));

        Assert.Equal(422, e.StatusCode);
        Assert.Contains("422", e.Message);
        Assert.Contains("POST", e.Message);
        Assert.Contains("pulls", e.Message);
        Assert.DoesNotContain(Token, e.Message);
        Assert.DoesNotContain("Authorization", e.Message);
        Assert.DoesNotContain("Validation Failed", e.Message);
        Assert.DoesNotContain(Token, e.ToString());
    }

    [Fact]
    public void Time_out_after_30_seconds()
    {
        var http = new HttpClient();

        GitHubRest.Configure(http, "WoozChucky/Avalon.Server", Token);

        Assert.Equal(TimeSpan.FromSeconds(30), http.Timeout);
    }

    [Fact]
    public async Task Send_a_failing_write_exactly_once_with_no_retry_from_the_default_resilience_handler()
    {
        int calls = 0;
        var handler = new StubHandler(_ =>
        {
            Interlocked.Increment(ref calls);
            return Json(HttpStatusCode.InternalServerError, "{}");
        });
        await using WebApplication app = BalanceTestHost.Build(
            extra: new Dictionary<string, string?> { ["Balance:GitHubToken"] = Token },
            services: s => s.AddHttpClient<IGitHub, GitHubRest>().ConfigurePrimaryHttpMessageHandler(() => handler));
        IGitHub github = app.Services.GetRequiredService<IGitHub>();

        GitHubApiException e = await Assert.ThrowsAsync<GitHubApiException>(() =>
            github.CreateBranchAsync("b", "abc", CancellationToken.None));

        Assert.Equal(500, e.StatusCode);
        Assert.Equal(1, calls);
        Assert.Single(handler.Requests);
    }
}
