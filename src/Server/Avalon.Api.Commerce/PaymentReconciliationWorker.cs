using System.Diagnostics.Metrics;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Commerce;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Commerce;

public sealed class PaymentReconciliationWorker(IPurchaseRepository purchases, IPaymentReconciliationService reconciliation,
    IOptions<CommerceConfiguration> options, TimeProvider clock, ILogger<PaymentReconciliationWorker> logger) : BackgroundService
{
    private readonly Meter _meter = new("Avalon.Commerce");
    private PaymentQueueStats _stats = new(0, 0, 0, null);
    private DateTime _nextSweep;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        _meter.CreateObservableGauge("avalon.commerce.events.pending", () => Volatile.Read(ref _stats).Pending);
        _meter.CreateObservableGauge("avalon.commerce.events.retry", () => Volatile.Read(ref _stats).Retry);
        _meter.CreateObservableGauge("avalon.commerce.events.needs_review", () => Volatile.Read(ref _stats).NeedsReview);
        _meter.CreateObservableGauge("avalon.commerce.events.oldest_pending_seconds", () =>
            Volatile.Read(ref _stats).OldestPending is { } oldest ? Math.Max(0, (clock.GetUtcNow().UtcDateTime - oldest).TotalSeconds) : 0);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), clock);
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            // Named by type only, here and below: a provider's or the store's exception may carry payment details.
            catch (Exception e) { logger.LogWarning("A payment reconciliation pass failed ({ExceptionType}).", e.GetType().Name); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled) return;
        IReadOnlyList<PaymentEventClaim> claims = await purchases.ClaimEventsAsync(clock.GetUtcNow().UtcDateTime, 10, TimeSpan.FromSeconds(120), ct);
        await Task.WhenAll(claims.Select(async claim =>
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(90));
            PaymentProcessingResult result;
            try { result = await reconciliation.ProcessAsync(claim, deadline.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { result = new(false, "PROVIDER_TIMEOUT"); }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                logger.LogWarning("Payment event {EventId} could not be reconciled ({ExceptionType}).", claim.Event.Id, e.GetType().Name);
                result = new(false, "RECONCILIATION_UNAVAILABLE");
            }
            await purchases.CompleteEventAsync(claim.Event.Id, claim.LeaseId, result, ct);
            if (!result.Completed) logger.LogInformation("Payment event {EventId} awaits reconciliation with {Reason}.", claim.Event.Id, result.FailureCode);
        }));
        DateTime now = clock.GetUtcNow().UtcDateTime;
        if (now >= _nextSweep)
        {
            _nextSweep = now.AddSeconds(60);
            IReadOnlyList<PaymentAttempt> candidates = await purchases.FindSweepCandidatesAsync(10, ct);
            await Task.WhenAll(candidates.Select(async attempt =>
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromSeconds(90));
                try { await reconciliation.SweepAsync(attempt, deadline.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { logger.LogWarning("Payment attempt {AttemptId} could not be reconciled ({ExceptionType}).", attempt.Id, e.GetType().Name); }
            }));
        }
        Volatile.Write(ref _stats, await purchases.ReadQueueStatsAsync(ct));
    }

    public override void Dispose() { _meter.Dispose(); base.Dispose(); }
}
