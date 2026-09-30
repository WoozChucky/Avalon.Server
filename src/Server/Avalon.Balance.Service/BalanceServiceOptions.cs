using System.ComponentModel.DataAnnotations;

namespace Avalon.Balance.Service;

/// <summary>The <c>Balance</c> configuration section. Only <see cref="SharedSecret" /> is required.</summary>
public sealed class BalanceServiceOptions
{
    public const string Section = "Balance";
    public const string HeaderName = "X-Balance-Secret";

    /// <summary>The value callers send in <c>X-Balance-Secret</c>.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Balance:SharedSecret is required and must be at least 32 characters")]
    [MinLength(32, ErrorMessage = "Balance:SharedSecret is required and must be at least 32 characters")]
    public string SharedSecret { get; set; } = "";

    /// <summary>A fine-grained GitHub token for exports; without it they answer 503.</summary>
    public string? GitHubToken { get; set; }

    public string Repository { get; set; } = "WoozChucky/Avalon.Server";

    public int MaxQueued { get; set; } = 3;

    public int MaxRunsPerRow { get; set; } = 1000;

    public int MaxOverrides { get; set; } = 500;

    /// <summary>False builds the worker paused: nothing drains the queue. A test seam.</summary>
    public bool RunWorker { get; set; } = true;

    public TimeSpan ResultTtl { get; set; } = TimeSpan.FromHours(1);
}
