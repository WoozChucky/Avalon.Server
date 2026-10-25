namespace Avalon.Crashes.Queries;

public interface ICrashReadStore
{
    Task<CrashGroupPage> ListGroupsAsync(CrashGroupFilter filter, DateTimeOffset now, CancellationToken ct);
    Task<CrashGroupDetail?> GetGroupAsync(CrashGroupId id, int reportsPage, int reportsPageSize, DateTimeOffset now, CancellationToken ct);
    Task<CrashReportDetail?> GetReportAsync(CrashReportId id, DateTimeOffset now, CancellationToken ct);
}
