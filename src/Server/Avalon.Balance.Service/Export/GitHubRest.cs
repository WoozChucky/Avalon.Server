using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Avalon.Balance.Service.Export;

/// <summary>
/// A typed HttpClient over the GitHub REST API of one repository. Registered without the standard resilience handler,
/// so a write is sent once. Errors carry the method, path and status; never the token, headers or response body.
/// </summary>
public sealed class GitHubRest(HttpClient http) : IGitHub
{
    public const string BaseBranch = "main";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>Sets the base address, the headers (including the token) and the timeout.</summary>
    public static void Configure(HttpClient client, string repository, string token)
    {
        client.BaseAddress = new Uri($"https://api.github.com/repos/{repository.Trim('/')}/");
        client.Timeout = Timeout;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("avalon-balance-service");
    }

    public async Task<(string Sha, string Text)?> GetFileAsync(string path, string reference, CancellationToken ct)
    {
        string relative = $"contents/{EscapePath(path)}?ref={Uri.EscapeDataString(reference)}";
        using HttpResponseMessage response = await http.GetAsync(relative, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        Ensure(response, "GET", $"contents/{path}");

        using JsonDocument document = await ReadAsync(response, "GET", $"contents/{path}", ct).ConfigureAwait(false);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("sha", out JsonElement sha) || sha.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("encoding", out JsonElement encoding) || !string.Equals(encoding.GetString(), "base64", StringComparison.Ordinal))
        {
            throw new GitHubApiException("GET", $"contents/{path}", (int)response.StatusCode, "unexpected response shape");
        }

        try
        {
            // GitHub wraps the base64 every 60 characters; the decoder ignores the newlines.
            return (sha.GetString()!, Utf8NoBom.GetString(Convert.FromBase64String(content.GetString()!)));
        }
        catch (FormatException)
        {
            throw new GitHubApiException("GET", $"contents/{path}", (int)response.StatusCode, "content is not base64");
        }
    }

    public async Task<bool> BranchExistsAsync(string branch, CancellationToken ct)
    {
        using HttpResponseMessage response = await http.GetAsync($"git/ref/heads/{EscapePath(branch)}", ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return false;
        Ensure(response, "GET", "git/ref/heads");
        return true;
    }

    public async Task CreateBranchAsync(string branch, string fromSha, CancellationToken ct)
    {
        using HttpResponseMessage response = await SendJsonAsync(HttpMethod.Post, "git/refs",
            new { @ref = $"refs/heads/{branch}", sha = fromSha }, ct).ConfigureAwait(false);
        Ensure(response, "POST", "git/refs");
    }

    public async Task PutFileAsync(string branch, string path, string text, string? existingSha, string message, CancellationToken ct)
    {
        var body = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["message"] = message,
            ["content"] = Convert.ToBase64String(Utf8NoBom.GetBytes(text)),
            ["branch"] = branch,
        };
        if (existingSha is not null)
            body["sha"] = existingSha;

        using HttpResponseMessage response = await SendJsonAsync(HttpMethod.Put, $"contents/{EscapePath(path)}", body, ct).ConfigureAwait(false);
        Ensure(response, "PUT", $"contents/{path}");
    }

    public async Task<string> OpenDraftPullRequestAsync(string branch, string title, string body, CancellationToken ct)
    {
        using HttpResponseMessage response = await SendJsonAsync(HttpMethod.Post, "pulls",
            new { title, body, head = branch, @base = BaseBranch, draft = true }, ct).ConfigureAwait(false);
        Ensure(response, "POST", "pulls");

        using JsonDocument document = await ReadAsync(response, "POST", "pulls", ct).ConfigureAwait(false);
        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("html_url", out JsonElement url) && url.ValueKind == JsonValueKind.String)
        {
            return url.GetString()!;
        }

        throw new GitHubApiException("POST", "pulls", (int)response.StatusCode, "unexpected response shape");
    }

    private Task<HttpResponseMessage> SendJsonAsync(HttpMethod method, string relative, object body, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, relative)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Utf8NoBom, "application/json"),
        };
        return http.SendAsync(request, ct);
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, string method, string path, CancellationToken ct)
    {
        try
        {
            await using Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            throw new GitHubApiException(method, path, (int)response.StatusCode, "response is not JSON");
        }
    }

    private static void Ensure(HttpResponseMessage response, string method, string path)
    {
        if (!response.IsSuccessStatusCode)
            throw new GitHubApiException(method, path, (int)response.StatusCode);
    }

    private static string EscapePath(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
}
