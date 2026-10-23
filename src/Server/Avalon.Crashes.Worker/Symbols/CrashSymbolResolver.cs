using Avalon.Crashes.Persistence;
using Avalon.Crashes.Worker.Processing;

namespace Avalon.Crashes.Worker.Symbols;

/// <summary>Resolve required runtime first, then optional symbols within one 60-second phase covering lock/cache/HTTP/conversion/copy/upload work.</summary>
public sealed class CrashSymbolResolver
{
    private readonly CrashSymbolOptions _options;
    private readonly ICrashSymbolStore _store;
    private readonly SymbolCache _cache;
    private readonly PublicSymbolClient _public;
    private readonly ICrashSymbolLock _lock;
    private readonly ICrashToolRunner _runner;
    private readonly CrashWorkspace _workspaces;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _converter = new(1, 1);

    public CrashSymbolResolver(CrashSymbolOptions options, ICrashSymbolStore store, SymbolCache cache,
        PublicSymbolClient publicClient, ICrashSymbolLock symbolLock, ICrashToolRunner runner, CrashWorkspace workspaces, TimeProvider? timeProvider = null)
    {
        options.Validate();
        _options = options;
        _store = store;
        _cache = cache;
        _public = publicClient;
        _lock = symbolLock;
        _runner = runner;
        _workspaces = workspaces;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Caller owns the main lease and eight-minute attempt token. Pass one budget already charged for the archive.</summary>
    public async Task<SymbolResolution> ResolveAsync(CrashInput input, CrashAttemptWorkspace workspace, SymbolDownloadBudget budget, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        budget.RestrictTo(_options.DownloadBytes);
        ModuleIdentity[] runtimes = input.Modules.Where(m => m.Name == input.Runtime.ImageName && m.ImageKey == input.Runtime.ImageKey
            && m.PdbName == input.Runtime.PdbName && m.PdbKey == input.Runtime.PdbKey).ToArray();
        if (runtimes.Length != 1 || !string.Equals(input.Runtime.ImageName, "runtime.exe", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(input.Runtime.PdbName, "runtime.pdb", StringComparison.OrdinalIgnoreCase))
        {
            throw new CrashSymbolException(CrashFailureCode.RuntimeIdentityMismatch);
        }
        ModuleIdentity runtime = runtimes[0];
        // Native Breakpad lookup cannot distinguish images sharing a debug key, even at different load ranges.
        // Include ineligible modules: native stackwalking can still look up their debug keys.
        var ambiguousKeys = input.Modules.Where(m => m.PdbName is not null && m.PdbKey is not null)
            .GroupBy(DebugKey, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(m => m.Name + "/" + m.ImageKey).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
            .Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ambiguousKeys.Contains(DebugKey(runtime)))
            throw new CrashSymbolException(CrashFailureCode.InvalidAnalysis);
        bool uploadFailed = await ResolveModuleAsync(runtime, workspace, budget, true, ct);
        var available = new List<ModuleIdentity> { runtime };
        CrashQualityFlags quality = input.Quality;
        using var deadline = new CancellationTokenSource(_options.PublicTimeout, _time);
        using var phase = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        int modules = 0;
        var resolved = new HashSet<string>(StringComparer.Ordinal);
        foreach (ModuleIdentity module in input.Modules)
        {
            ct.ThrowIfCancellationRequested();
            if (module == runtime)
                continue;
            if (!_options.PublicEnabled || !PublicSymbolClient.IsEligible(module)
                || ambiguousKeys.Contains(DebugKey(module))
                || string.Equals(module.PdbKey, runtime.PdbKey, StringComparison.OrdinalIgnoreCase)
                || modules >= _options.PublicModules || deadline.IsCancellationRequested)
            {
                quality |= CrashQualityFlags.MissingSystemSymbols;
                continue;
            }
            string key = SymbolFiles.LockIdentity(module);
            if (resolved.Contains(key))
            {
                available.Add(module);
                continue;
            }
            modules++;
            try
            {
                uploadFailed |= await ResolveModuleAsync(module, workspace, budget, false, phase.Token);
                available.Add(module);
                resolved.Add(key);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                quality |= CrashQualityFlags.MissingSystemSymbols;
            }
            catch (Exception e) when (e is CrashSymbolException or IOException or System.Data.Common.DbException)
            {
                quality |= CrashQualityFlags.MissingSystemSymbols;
            }
        }
        ct.ThrowIfCancellationRequested();
        return new(Path.Combine(workspace.DirectoryPath, "symbols"), quality,
            new(CrashSymbolOptions.StackwalkVersion, CrashSymbolOptions.ConverterVersion, _options.CacheVersion), available.AsReadOnly(), uploadFailed);
    }

    private static string DebugKey(ModuleIdentity module) => module.PdbName + "/" + module.PdbKey;

    private async Task<bool> ResolveModuleAsync(ModuleIdentity module, CrashAttemptWorkspace main, SymbolDownloadBudget budget, bool required, CancellationToken ct)
    {
        SymbolFiles.Require(module);
        string? local = await _cache.MaterializeAsync(module, main, ct);
        if (local is not null)
            return await UploadAsync(module, local, ct);
        await using IAsyncDisposable lease = await _lock.AcquireAsync(_options.CacheVersion, SymbolFiles.LockIdentity(module), ct);
        // Another worker may have published while this caller waited for the database lock.
        local = await _cache.MaterializeAsync(module, main, ct);
        if (local is not null)
            return await UploadAsync(module, local, ct);
        await using CrashAttemptWorkspace conversion = await _workspaces.CreateAsync(Guid.NewGuid(), ct);
        string? remote = null;
        SymbolCacheMetadata? metadata = null;
        try
        {
            remote = await conversion.WriteExternalAsync("remote.sym", DownloadCap(budget), async (path, cap, token) =>
            {
                metadata = await _store.DownloadConvertedAsync(module, _options.CacheVersion, path, cap, budget, token);
            }, ct);
            if (await _cache.ValidateAsync(module, metadata, remote, ct))
            {
                await _cache.PublishAsync(module, remote, ct);
                local = await _cache.MaterializeAsync(module, main, ct);
                if (local is not null)
                    return false;
            }
        }
        catch (Exception e) when (e is CrashSymbolException or InvalidDataException or System.Text.DecoderFallbackException)
        {
            // Converted storage is an optional accelerator; immutable originals remain authoritative.
        }
        finally
        {
            if (remote is not null)
                File.Delete(remote);
        }
        string pdb;
        if (required)
        {
            string image = await conversion.WriteExternalAsync(module.Name, DownloadCap(budget),
                (path, cap, token) => _store.DownloadOriginalAsync(module, false, path, cap, budget, token), ct);
            pdb = await conversion.WriteExternalAsync(module.PdbName!, DownloadCap(budget),
                (path, cap, token) => _store.DownloadOriginalAsync(module, true, path, cap, budget, token), ct);
            if (!await SymbolIdentityValidator.ValidatePairAsync(module, image, pdb, ct))
                throw new CrashSymbolException(CrashFailureCode.MissingRuntimeSymbols);
        }
        else
        {
            PublicSymbolResult result = await _public.FetchAsync(module, conversion, budget, ct);
            if (result.Status != PublicSymbolStatus.Available)
                throw new CrashSymbolException(CrashFailureCode.MissingRuntimeSymbols);
            pdb = result.PdbPath!;
        }
        await _converter.WaitAsync(ct);
        ToolRunResult converted;
        try
        {
            converted = await _runner.RunAsync(new(CrashToolKind.DumpSymbols, ["--inlines", "--check-cfi", pdb], conversion,
                TimeSpan.FromSeconds(120), 90, 256L * 1024 * 1024, 64 * 1024), ct);
        }
        finally { _converter.Release(); }
        ct.ThrowIfCancellationRequested();
        if (converted.Failure != ToolFailureKind.None || converted.OutputPath is null)
        {
            throw new CrashSymbolException(converted.Failure switch
            {
                ToolFailureKind.WallTime or ToolFailureKind.CpuTime => CrashFailureCode.ToolTimeout,
                ToolFailureKind.OutputLimit or ToolFailureKind.ScratchLimit => CrashFailureCode.ResourceLimit,
                ToolFailureKind.ToolIdentity or ToolFailureKind.InvalidInvocation => CrashFailureCode.ConfigurationInvalid,
                _ => CrashFailureCode.ToolFailed
            });
        }
        try
        {
            if (!await SymbolIdentityValidator.ValidateConvertedAsync(module, converted.OutputPath, ct))
                throw new CrashSymbolException(CrashFailureCode.ToolFailed);
        }
        catch (Exception e) when (e is InvalidDataException or System.Text.DecoderFallbackException)
        {
            throw new CrashSymbolException(CrashFailureCode.ToolFailed);
        }
        await _cache.PublishAsync(module, converted.OutputPath, ct);
        local = await _cache.MaterializeAsync(module, main, ct);
        if (local is null)
            throw new CrashSymbolException(CrashFailureCode.MissingRuntimeSymbols);
        return await UploadAsync(module, local, ct);
    }

    private long DownloadCap(SymbolDownloadBudget budget)
    {
        long cap = Math.Min(_options.ArtifactBytes, budget.RemainingBytes);
        if (cap <= 0)
            throw new CrashSymbolException(CrashFailureCode.ResourceLimit);
        return cap;
    }

    private async Task<bool> UploadAsync(ModuleIdentity module, string local, CancellationToken ct)
    {
        var metadata = new SymbolCacheMetadata(module.Name, module.ImageKey, module.PdbName!, module.PdbKey!, _options.CacheVersion,
            CrashSymbolOptions.ConverterVersion, await SymbolFiles.DigestAsync(local, ct), new FileInfo(local).Length);
        try
        {
            await _store.UploadConvertedAsync(module, metadata, local, ct);
            return false;
        }
        catch (Exception e) when (e is CrashSymbolException or IOException)
        {
            return true;
        }
    }
}
