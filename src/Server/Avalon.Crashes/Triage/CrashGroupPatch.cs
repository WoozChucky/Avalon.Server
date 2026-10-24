namespace Avalon.Crashes.Triage;

public readonly record struct FieldChange<T>(bool IsSpecified, T? Value);

public sealed record CrashGroupPatch(
    long TriageRevision,
    FieldChange<CrashGroupStatus?> Status = default,
    FieldChange<string> FixedInVersion = default,
    FieldChange<string> IssueUrl = default,
    FieldChange<string> Notes = default);
