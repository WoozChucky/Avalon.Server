using System.Data.Common;
using Avalon.Crashes.Analysis;
using Avalon.Crashes.Persistence;
using Avalon.Crashes.Worker.Storage;
using Avalon.Crashes.Worker.Symbols;

namespace Avalon.Crashes.Worker.Processing;

public interface ICrashAnalysisPipeline
{
    Task<CrashAnalysis> AnalyzeAsync(StoredCrashObject source, CrashAttemptWorkspace workspace, CancellationToken ct);
}

public enum CrashBackoffDependency { Unknown, Discovery, CrashRead, RequiredSymbols, Tools }
public sealed record CrashProcessingBackoffState(CrashFailureCode Reason, DateTimeOffset RetryAt,
    CrashBackoffDependency Dependency = CrashBackoffDependency.Unknown);

public sealed class CrashProcessingBackoff
{
    private CrashProcessingBackoffState? _state;
    public CrashProcessingBackoffState? State => Volatile.Read(ref _state);

    /// <summary>A single finite outage reason and bounded retry deadline; no report or exception content.</summary>
    public void Degrade(CrashFailureCode reason, DateTimeOffset now, CrashBackoffDependency dependency = CrashBackoffDependency.Unknown)
    {
        if (reason is not (CrashFailureCode.AccessDenied or CrashFailureCode.ConfigurationInvalid))
            throw new ArgumentOutOfRangeException(nameof(reason));
        Interlocked.Exchange(ref _state, new(reason, now.AddMinutes(5), dependency));
    }

    /// <summary>Call only after a successful probe of the failing dependency, or a fully successful analysis.</summary>
    public void Clear() => Interlocked.Exchange(ref _state, null);

    /// <summary>A probe may clear only the same outage it observed before starting, never a newer or unrelated failure.</summary>
    public void Recovered(CrashProcessingBackoffState? observed, CrashBackoffDependency dependency)
    {
        if (dependency != CrashBackoffDependency.Unknown && observed?.Dependency == dependency)
            Interlocked.CompareExchange(ref _state, null, observed);
    }
}

public sealed class CrashProcessor
{
    private readonly ICrashQueue _queue;
    private readonly ICrashObjectStore _objects;
    private readonly CrashWorkspace _workspaces;
    private readonly ICrashAnalysisPipeline _pipeline;
    private readonly CrashRetryPolicy _retry;
    private readonly CrashProcessingBackoff _backoff;
    private readonly TimeProvider _time;

    public CrashProcessor(ICrashQueue queue, ICrashObjectStore objects, CrashWorkspace workspaces,
        ICrashAnalysisPipeline pipeline, CrashRetryPolicy retry, CrashProcessingBackoff backoff, TimeProvider? timeProvider = null)
    {
        _queue = queue;
        _objects = objects;
        _workspaces = workspaces;
        _pipeline = pipeline;
        _retry = retry;
        _backoff = backoff;
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task ProcessAsync(CrashLease lease, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        TimeSpan remainingLease = lease.LeaseUntil - _time.GetUtcNow();
        if (remainingLease <= TimeSpan.Zero || lease.ExpiresAt <= _time.GetUtcNow())
            return;

        using var lost = new CancellationTokenSource();
        using var leaseDeadline = new CancellationTokenSource(remainingLease, _time);
        using var ownership = CancellationTokenSource.CreateLinkedTokenSource(ct, lost.Token, leaseDeadline.Token);
        DateTimeOffset processedUntil = _time.GetUtcNow().AddMinutes(8);
        using var attemptDeadline = new CancellationTokenSource(TimeSpan.FromMinutes(8), _time);
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ownership.Token, attemptDeadline.Token);
        using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(ownership.Token);
        Task heartbeat = RenewAsync(lease, leaseDeadline, lost, heartbeatStop.Token);
        try
        {
            CrashAttemptResult result;
            try
            {
                await using CrashAttemptWorkspace workspace = await _workspaces.CreateAsync(lease.ReportId, attempt.Token);
                CrashAnalysis analysis = await _pipeline.AnalyzeAsync(lease.Source, workspace, attempt.Token);
                attempt.Token.ThrowIfCancellationRequested();
                SignatureIdentity signature = CrashSignature.Create(analysis);
                if (lease.ExpiresAt <= _time.GetUtcNow())
                    return;

                // Confirm late deletion/change before accepting processed content.
                StoredCrashObject? current = await _objects.HeadAsync(lease.Source.Key, attempt.Token);
                attempt.Token.ThrowIfCancellationRequested();
                if (current is null)
                {
                    result = _retry.Decide(CrashFailureCode.ObjectMissing, lease, _time.GetUtcNow());
                }
                else if (current != lease.Source)
                {
                    result = _retry.Decide(CrashFailureCode.ObjectChanged, lease, _time.GetUtcNow());
                }
                else
                {
                    result = CrashAttemptResult.Processed(analysis, signature, processedUntil);
                    _backoff.Clear();
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && ownership.IsCancellationRequested) { return; }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && attemptDeadline.IsCancellationRequested)
            {
                result = _retry.Decide(CrashFailureCode.ToolTimeout, lease, _time.GetUtcNow());
            }
            catch (CrashInputException ex) { result = Failure(ex.Code, lease); }
            catch (CrashSymbolException ex) { result = Failure(ex.Code, lease, CrashBackoffDependency.RequiredSymbols); }
            catch (CrashStorageException ex) { result = Failure(ex.Code, lease, CrashBackoffDependency.CrashRead); }
            catch (CrashScratchLimitException) { result = Failure(CrashFailureCode.ResourceLimit, lease); }
            catch (Exception ex) when (IsInfrastructure(ex)) { result = Failure(CrashFailureCode.StorageUnavailable, lease); }

            ct.ThrowIfCancellationRequested();
            if (ownership.IsCancellationRequested || lease.ExpiresAt <= _time.GetUtcNow())
                return;

            // Finalization is outside failure classification. An outage never becomes a local terminal result.
            // Replaying exactly this token/result reconciles an uncertain commit without recounting.
            try
            {
                bool accepted = await _queue.CompleteAsync(lease, result, _time.GetUtcNow(), result.Status == CrashReportStatus.Processed ? attempt.Token : ownership.Token);
                if (!accepted && attemptDeadline.IsCancellationRequested && !ownership.IsCancellationRequested)
                    await ReconcileAsync(lease, result, true, ownership.Token);
            }
            catch (Exception ex) when (IsInfrastructure(ex) && !ownership.IsCancellationRequested)
            {
                await ReconcileAsync(lease, result, attemptDeadline.IsCancellationRequested, ownership.Token);
            }
            catch (OperationCanceledException) when (attemptDeadline.IsCancellationRequested && !ownership.IsCancellationRequested)
            {
                await ReconcileAsync(lease, result, true, ownership.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && ownership.IsCancellationRequested) { }
        finally
        {
            await heartbeatStop.CancelAsync();
            await heartbeat;
        }
    }

    private async Task ReconcileAsync(CrashLease lease, CrashAttemptResult result, bool timedOut, CancellationToken ct)
    {
        // The processed deadline forbids a new successful commit after timeout, but accepted tokens still reconcile.
        bool accepted;
        try
        {
            accepted = await _queue.CompleteAsync(lease, result, _time.GetUtcNow(), ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && result.ProcessedDeadline <= _time.GetUtcNow())
        {
            // The reconciliation started before the deadline and its commit acknowledgement was cancelled.
            // A fresh replay can only observe an existing completion, never create a new processed result.
            accepted = await _queue.CompleteAsync(lease, result, _time.GetUtcNow(), ct);
            timedOut = true;
        }
        if (!accepted && timedOut && result.Status == CrashReportStatus.Processed)
            await _queue.CompleteAsync(lease, _retry.Decide(CrashFailureCode.ToolTimeout, lease, _time.GetUtcNow()), _time.GetUtcNow(), ct);
    }

    private CrashAttemptResult Failure(CrashFailureCode code, CrashLease lease, CrashBackoffDependency dependency = CrashBackoffDependency.Unknown)
    {
        if (code is CrashFailureCode.AccessDenied or CrashFailureCode.ConfigurationInvalid)
            _backoff.Degrade(code, _time.GetUtcNow(), code == CrashFailureCode.ConfigurationInvalid ? CrashBackoffDependency.Unknown : dependency);
        return _retry.Decide(code, lease, _time.GetUtcNow());
    }

    private async Task RenewAsync(CrashLease lease, CancellationTokenSource deadline, CancellationTokenSource lost, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), _time, ct);
                if (!await _queue.RenewAsync(lease, _time.GetUtcNow(), ct))
                {
                    await lost.CancelAsync();
                    return;
                }
                TimeSpan remaining = lease.ExpiresAt - _time.GetUtcNow();
                deadline.CancelAfter(remaining < TimeSpan.FromMinutes(10) ? remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero : TimeSpan.FromMinutes(10));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch
        {
            await lost.CancelAsync();
            throw;
        }
    }

    private static bool IsInfrastructure(Exception ex) => ex is DbException or TimeoutException or IOException;
}
