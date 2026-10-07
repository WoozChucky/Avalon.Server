using Avalon.Balance.Service.Export;
using Xunit;

namespace Avalon.Balance.Service.UnitTests;

/// <summary>An in-memory IGitHub that records every call in order.</summary>
internal sealed class FakeGitHub : IGitHub
{
    public Dictionary<string, (string Sha, string Text)> FilesAtCommit { get; } = new(StringComparer.Ordinal);

    public HashSet<string> Branches { get; } = new(StringComparer.Ordinal);

    public List<string> Calls { get; } = [];

    public List<(string Branch, string Path, string Text, string? ExistingSha, string Message)> Puts { get; } = [];

    public List<(string Branch, string Title, string Body)> PullRequests { get; } = [];

    public string? CreatedBranch { get; private set; }

    public string? CreatedFrom { get; private set; }

    /// <summary>Throws from the named call (GetFile, BranchExists, CreateBranch, PutFile or OpenPullRequest).</summary>
    public (string Call, int Status)? Fail { get; set; }

    private void Step(string call)
    {
        Calls.Add(call);
        if (Fail is { } f && f.Call == call)
            throw new GitHubApiException("TEST", call, f.Status);
    }

    public Task<(string Sha, string Text)?> GetFileAsync(string path, string reference, CancellationToken ct)
    {
        Step("GetFile");
        Assert.Equal(Commit, reference);
        return Task.FromResult<(string, string)?>(FilesAtCommit.TryGetValue(path, out (string Sha, string Text) file) ? file : null);
    }

    public const string Commit = "abc123def456";

    public Task<bool> BranchExistsAsync(string branch, CancellationToken ct)
    {
        Step("BranchExists");
        return Task.FromResult(Branches.Contains(branch));
    }

    public Task CreateBranchAsync(string branch, string fromSha, CancellationToken ct)
    {
        Step("CreateBranch");
        CreatedBranch = branch;
        CreatedFrom = fromSha;
        Branches.Add(branch);
        return Task.CompletedTask;
    }

    public Task PutFileAsync(string branch, string path, string text, string? existingSha, string message, CancellationToken ct)
    {
        Step("PutFile");
        Puts.Add((branch, path, text, existingSha, message));
        return Task.CompletedTask;
    }

    public Task<string> OpenDraftPullRequestAsync(string branch, string title, string body, CancellationToken ct)
    {
        Step("OpenPullRequest");
        PullRequests.Add((branch, title, body));
        return Task.FromResult("https://github.com/WoozChucky/Avalon.Server/pull/999");
    }
}
