namespace Avalon.Balance.Service.Export;

/// <summary>
/// GitHub answered with a status the call did not expect. The message holds the method, the path and the status only:
/// never the token, the headers or GitHub's response body.
/// </summary>
public sealed class GitHubApiException : Exception
{
    public GitHubApiException(string method, string path, int? statusCode, string? detail = null)
        : base($"GitHub {method} {path} failed"
            + (statusCode is { } s ? $" with HTTP {s}" : "")
            + (detail is null ? "" : $": {detail}"))
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}
