namespace Avalon.Crashes.Worker.Discovery;

public sealed class CrashDiscoveryOptions
{
    public const int PageBudget = 100;
    public const int BackfillBudget = 25;
    public const int VersionBatchSize = Avalon.Crashes.Persistence.DiscoveryCursor.VersionBatchSize;
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
}
