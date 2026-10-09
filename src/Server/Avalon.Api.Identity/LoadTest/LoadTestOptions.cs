namespace Avalon.Api.Identity.LoadTest;

/// <summary>
/// <c>Application:LoadTest</c>: whether identity provisions load-test bot accounts, and how many it holds at most.
/// Validated on start.
/// </summary>
public sealed class LoadTestOptions
{
    public const string Section = "Application:LoadTest";

    /// <summary>When false (the default), the load-test account endpoint answers 404.</summary>
    public bool Enabled { get; set; }

    /// <summary>The most load-test accounts that may exist at once, across every run. At least 1.</summary>
    public int MaxAccounts { get; set; } = 5000;
}
