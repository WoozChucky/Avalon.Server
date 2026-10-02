namespace Avalon.Balance.Service.Export;

/// <summary>The few GitHub operations an export needs, so the composer can be tested without a network.</summary>
public interface IGitHub
{
    /// <summary>The file's blob sha and text at <paramref name="reference" />, or null when it does not exist there.</summary>
    Task<(string Sha, string Text)?> GetFileAsync(string path, string reference, CancellationToken ct);

    Task<bool> BranchExistsAsync(string branch, CancellationToken ct);

    Task CreateBranchAsync(string branch, string fromSha, CancellationToken ct);

    Task PutFileAsync(string branch, string path, string text, string? existingSha, string message, CancellationToken ct);

    /// <returns>The pull request's web URL.</returns>
    Task<string> OpenDraftPullRequestAsync(string branch, string title, string body, CancellationToken ct);
}
