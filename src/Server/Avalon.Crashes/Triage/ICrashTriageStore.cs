namespace Avalon.Crashes.Triage;

public interface ICrashTriageStore
{
    Task<CrashTriageResult> UpdateAsync(CrashGroupId id, CrashGroupPatch patch, DateTimeOffset now, CancellationToken ct);
}
