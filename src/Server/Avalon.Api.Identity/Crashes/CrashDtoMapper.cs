using System.Reflection;
using System.Text.Json.Serialization;
using System.Text;
using Avalon.Api.Contract.Crashes;
using Avalon.Crashes.Queries;
using Avalon.Crashes.Triage;
using Avalon.Crashes;

namespace Avalon.Api.Identity.Crashes;

public static class CrashDtoMapper
{
    private const CrashQualityFlags KnownQuality = CrashQualityFlags.MissingSystemSymbols | CrashQualityFlags.ScanFrames
        | CrashQualityFlags.Truncated | CrashQualityFlags.MetadataDisagreement | CrashQualityFlags.VersionComparisonUnavailable | CrashQualityFlags.SanitizedText;

    public static CrashGroupPageDto Page(CrashGroupPage page) => new(page.Items.Select(Group).ToArray(), page.Page,
        page.PageSize, page.Total, Utc(page.AsOf), page.RetentionDays, Utc(page.EffectiveSince));

    public static CrashGroupDto Group(CrashGroupSummary group) => new(group.Id.Value, group.Signature, group.SignatureVersion,
        EnumName(group.Status), group.FixedInVersion, group.IssueUrl, group.IsRegression, group.RegressionCount,
        Utc(group.LastRegressionAt), group.TriageRevision, Utc(group.TriagedAt), Utc(group.FixedAt), Utc(group.FirstSeen),
        Utc(group.LastSeen), group.FirstVersion, group.LastVersion, group.ReportCount, group.RetainedReportCount,
        group.MatchingReportCount, group.DailyCounts.Select(c => new CrashDailyCountDto(c.Date, c.Count)).ToArray());

    public static CrashGroupTriageDto Triage(CrashGroupTriage group) => new(group.Id.Value, EnumName(group.Status),
        group.FixedInVersion, group.IssueUrl, group.Notes, group.IsRegression, group.RegressionCount,
        Utc(group.LastRegressionAt), group.TriageRevision, Utc(group.TriagedAt), Utc(group.FixedAt));

    public static CrashGroupDetailDto Detail(CrashGroupDetail detail) => new(Group(detail.Group), detail.Notes,
        Utc(detail.AsOf), Utc(detail.WindowStart), Utc(detail.WindowEnd), detail.RetainedReportCount,
        detail.LatestReportId?.Value, Utc(detail.LatestStackReceivedAt), detail.LatestStack is null ? null : Thread(detail.LatestStack),
        detail.LatestStackQuality is { } quality ? Quality(quality, HasUnknownTrust(detail.LatestStack)) : null,
        detail.ByVersion.Select(c => new CrashVersionCountDto(c.Version, c.Count)).ToArray(),
        detail.ByGpuDriver.Select(c => new CrashGpuDriverCountDto(c.Module, c.Version, c.Source, c.Count)).ToArray(),
        detail.ByOsBuild.Select(c => new CrashOsBuildCountDto(c.Build, c.Count)).ToArray(),
        new CrashReportPageDto(detail.Reports.Items.Select(r => new CrashReportSummaryDto(r.Id.Value, Utc(r.ReceivedAt),
            Utc(r.ExpiresAt), r.Version, r.ExceptionCode, Quality(r.Quality), r.CrashingModule)).ToArray(),
            detail.Reports.Page, detail.Reports.PageSize, detail.Reports.Total));

    public static CrashReportDto Report(CrashReportDetail report) => new(report.Id.Value, EnumName(report.Status),
        report.Reason is { } reason ? EnumName(reason) : null, Utc(report.ReceivedAt), Utc(report.ExpiresAt), Utc(report.ProcessedAt),
        report.GroupId?.Value, report.Version, report.GitHash, Utc(report.ClientCrashedAt), report.ExceptionCode,
        report.ExceptionAddress, report.CrashingModule, report.OsVersion, report.OsBuild, report.Cpu, report.GpuDriverModule,
        report.GpuDriverVersion, report.GpuDriverSource,
        Quality(report.Quality, !Enum.IsDefined(report.Status) || report.Reason is { } r && !Enum.IsDefined(r)
            || report.Analysis?.Threads.Any(HasUnknownTrust) == true),
        report.Analysis is null ? null : Stack(report.Analysis), BoundedLog(report.LogTail));

    private static CrashStackDto Stack(CrashAnalysis analysis) => new(analysis.SchemaVersion,
        new CrashIdentityDto(analysis.Crash.Reason, analysis.Crash.Code, analysis.Crash.Address, analysis.Crash.CrashingThreadIndex, analysis.Crash.ThreadId),
        new CrashSystemDto(analysis.System.Os, analysis.System.OsVersion, analysis.System.OsBuild, analysis.System.CpuArchitecture, analysis.System.CpuInfo),
        analysis.Threads.Select(Thread).ToArray(),
        analysis.Modules.Select(m => new CrashModuleDto(m.Name, m.ImageKey, m.PdbName, m.PdbKey, m.Version, m.BaseAddress, m.Size, m.SymbolsAvailable)).ToArray(),
        new CrashMetadataDto(analysis.Metadata.Schema, analysis.Metadata.Exe, analysis.Metadata.Version, analysis.Metadata.GitHash,
            analysis.Metadata.ExceptionCode, analysis.Metadata.ExceptionAddress, analysis.Metadata.ThreadId, Utc(analysis.Metadata.Time)),
        Quality(analysis.Quality, analysis.Threads.Any(HasUnknownTrust)),
        new CrashProcessorDto(analysis.Processor.StackwalkVersion, analysis.Processor.ConverterVersion, analysis.Processor.CacheVersion));

    private static CrashThreadDto Thread(CrashThread thread) => new(thread.Index, thread.ThreadId,
        thread.Frames.Select(f => new CrashFrameDto(f.Module, f.ModuleOffset, f.Function, f.Source, f.SourceLine,
            f.InlineFrames.Select(i => new CrashInlineFrameDto(i.Function, i.Source, i.SourceLine)).ToArray(), Trust(f.Trust), f.ImageKey)).ToArray());

    private static string? Trust(string? trust) => trust is "context" or "cfi" or "frame_pointer" or "scan" or "cfi_scan" or "prewalk" or "none" ? trust : null;
    private static bool HasUnknownTrust(CrashThread? thread) => thread?.Frames.Any(f => f.Trust is not null && Trust(f.Trust) is null) == true;

    private static IReadOnlyList<string> Quality(CrashQualityFlags flags, bool unknown = false)
    {
        var values = new List<string>();
        foreach (CrashQualityFlags flag in Enum.GetValues<CrashQualityFlags>())
        {
            if (flag != CrashQualityFlags.None && (flags & flag) != 0) values.Add(EnumName(flag)!);
        }
        if (unknown || (flags & ~KnownQuality) != 0) values.Add("unknownNativeValue");
        return values;
    }

    private static string? EnumName<T>(T value) where T : struct, Enum => Enum.IsDefined(value)
        ? typeof(T).GetField(value.ToString())?.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name : null;

    private static DateTimeOffset Utc(DateTimeOffset value) => value.ToUniversalTime();
    private static DateTimeOffset? Utc(DateTimeOffset? value) => value?.ToUniversalTime();

    private static string? BoundedLog(string? value)
    {
        if (value is null || Encoding.UTF8.GetByteCount(value) <= CrashLimits.LogBytes) return value;
        int length = Math.Min(value.Length, CrashLimits.LogBytes);
        while (Encoding.UTF8.GetByteCount(value.AsSpan(0, length)) > CrashLimits.LogBytes) length--;
        if (length > 0 && char.IsHighSurrogate(value[length - 1])) length--;
        return value[..length];
    }
}
