namespace Avalon.Crashes.Persistence;

/// <summary>Best-effort post-commit observations. No raw report material crosses this boundary.</summary>
public interface ICrashQueueObserver
{
    void Terminal(string? version, CrashReportStatus status);
    void GroupCreated();
    void Retry();
    void LeaseRecovered();
}
