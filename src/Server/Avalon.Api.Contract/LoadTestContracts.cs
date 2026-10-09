using System.ComponentModel.DataAnnotations;

namespace Avalon.Api.Contract;

/// <summary>
/// <c>POST /admin/load-test/accounts</c>: a run of <see cref="Count"/> load-test bot accounts, all signing in with
/// <see cref="Password"/>.
/// </summary>
public sealed record CreateLoadTestRunRequest
{
    /// <summary>Three ASCII letters, upper-cased; generated when absent. A run id already used is refused.</summary>
    public string? RunId { get; init; }

    [Range(1, 1000)] public int Count { get; init; }

    /// <summary>The bots' password, under the registration rules: measured trimmed, the form it is hashed in.</summary>
    [Required, TrimmedMinLength(8)] public string Password { get; init; } = "";

    /// <summary>The calling admin's own current password, as for <c>POST /pat/admin</c>.</summary>
    public string CurrentPassword { get; init; } = "";
}

/// <summary>The run created: its id and its accounts' usernames, in index order. The password is never echoed.</summary>
public sealed record LoadTestRunCreated(string RunId, IReadOnlyList<string> Accounts);
