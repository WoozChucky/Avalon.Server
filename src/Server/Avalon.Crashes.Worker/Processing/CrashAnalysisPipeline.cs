using Avalon.Crashes.Analysis;
using Avalon.Crashes.Validation;
using Avalon.Crashes.Worker.Storage;
using Avalon.Crashes.Worker.Symbols;

namespace Avalon.Crashes.Worker.Processing;

public sealed class CrashAnalysisPipeline(ICrashObjectStore objects, CrashArchiveReader archives,
    CrashSymbolResolver symbols, ICrashToolRunner runner, CrashSymbolOptions options) : ICrashAnalysisPipeline
{
    public async Task<CrashAnalysis> AnalyzeAsync(StoredCrashObject source, CrashAttemptWorkspace workspace, CancellationToken ct)
    {
        if (!CrashObjectKey.TryParse(source.Key, out CrashObjectKey? key))
            throw new CrashInputException(CrashFailureCode.InvalidObjectKey);
        string archive = await workspace.WriteExternalAsync("report.zip", CrashLimits.ArchiveBytes,
            (path, cap, token) => objects.DownloadAsync(source, path, cap, token), ct);
        long bytes = new FileInfo(archive).Length;
        if (bytes != source.Size)
            throw new CrashStorageException(CrashFailureCode.ObjectChanged);
        var budget = new SymbolDownloadBudget(options.DownloadBytes);
        budget.Charge(bytes);
        CrashInput? extracted = null;
        await workspace.WriteExternalAsync("dump.dmp", CrashLimits.ArchiveBytes, async (path, _, token) =>
        {
            await using var zip = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            extracted = await archives.ReadAsync(zip, key!.Version, Path.GetDirectoryName(path)!, token);
        }, ct);
        CrashInput input = extracted!;

        SymbolResolution resolution = await symbols.ResolveAsync(input, workspace, budget, ct);
        ToolRunResult walk = await runner.RunAsync(new(CrashToolKind.Stackwalk,
            ["--json", input.DumpPath, resolution.BreakpadRoot], workspace, TimeSpan.FromSeconds(60), 45, 16L * 1024 * 1024, 64 * 1024), ct);
        ct.ThrowIfCancellationRequested();
        if (walk.Failure != ToolFailureKind.None || walk.ExitCode != 0 || walk.OutputPath is null)
        {
            throw new CrashSymbolException(walk.Failure switch
            {
                ToolFailureKind.WallTime or ToolFailureKind.CpuTime => CrashFailureCode.ToolTimeout,
                ToolFailureKind.ScratchLimit or ToolFailureKind.OutputLimit => CrashFailureCode.ResourceLimit,
                ToolFailureKind.InvalidInvocation or ToolFailureKind.ToolIdentity => CrashFailureCode.ConfigurationInvalid,
                _ => CrashFailureCode.ToolFailed,
            });
        }

        var output = new FileInfo(walk.OutputPath);
        if (output.Length > 16L * 1024 * 1024)
            throw new CrashInputException(CrashFailureCode.ResourceLimit);
        byte[] json = await File.ReadAllBytesAsync(walk.OutputPath, ct);
        return StackwalkProjector.Project(json, input with { Quality = resolution.Quality }, resolution.Processor);
    }
}
