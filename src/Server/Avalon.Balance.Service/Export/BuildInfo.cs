namespace Avalon.Balance.Service.Export;

/// <summary>What the service was built from. A null commit (a build without a sha) means exports are unavailable.</summary>
public sealed record BuildInfo(string? Commit)
{
    public static BuildInfo FromAssembly()
    {
        string commit = BalanceHost.ReadVersion().Commit;
        return new BuildInfo(string.Equals(commit, "unknown", StringComparison.Ordinal) ? null : commit);
    }
}
